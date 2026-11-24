using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using StockCount.Contracts;
using StockCount.Core;
using Swashbuckle.AspNetCore.SwaggerGen;
using Swashbuckle.AspNetCore.SwaggerUI;

namespace StockCount.Api;

public static class ApiDocumentation
{
    public static void AddStockCountDocumentation(this IServiceCollection services)
    {
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(options =>
        {
            options.SwaggerDoc("v1", new OpenApiInfo
            {
                Title = "StockCount · 库存盘点接口文档",
                Version = "v1",
                Description = "适用于 PDA 与 Sage X3 的库存盘点 API。业务接口须指定帐套 Test（XTEST）或 Production（COB）。可信内网免密模式无需设备密钥；旧版认证可填写请求头。页面仅允许 GET 在线测试，POST 提交与扫码接口提供完整参数文档及示例，不在页面直接执行，以免误写正式帐套。数据库就绪不代表写入权限已通过。"
            });
            options.OperationFilter<ChineseOperationFilter>();
            options.SchemaFilter<ChineseSchemaFilter>();
        });
    }

    public static void UseStockCountDocumentation(this WebApplication app)
    {
        // Documentation is only available to direct private IPv4 or loopback peers.
        app.Use(async (context, next) =>
        {
            var path = context.Request.Path;
            if (path == "/" || path.StartsWithSegments("/docs") ||
                path.StartsWithSegments("/swagger") || path.StartsWithSegments("/openapi"))
            {
                if (!ApiEndpointPolicy.IsPrivateLanAddress(context.Connection.RemoteIpAddress))
                {
                    context.Response.StatusCode = 403;
                    return;
                }
                context.Response.Headers.CacheControl = "no-store";
            }
            await next();
        });
        app.UseSwagger(options => options.RouteTemplate = "openapi/{documentName}.json");
        app.UseSwaggerUI(options =>
        {
            options.RoutePrefix = "docs";
            options.SwaggerEndpoint("/openapi/v1.json", "StockCount 库存盘点 v1");
            options.DocumentTitle = "StockCount · 中文 API 文档";
            options.DocExpansion(DocExpansion.List);
            options.DisplayRequestDuration();
            options.SupportedSubmitMethods(SubmitMethod.Get);
            options.InjectJavascript("/docs/zh-cn.js");
        });
        app.MapGet("/", () => Results.Redirect("/docs/index.html")).ExcludeFromDescription();
        app.MapGet("/swagger", () => Results.Redirect("/docs/index.html")).ExcludeFromDescription();
        app.MapGet("/swagger/index.html", () => Results.Redirect("/docs/index.html")).ExcludeFromDescription();
        app.MapGet("/docs/zh-cn.js", () =>
        {
            using var stream = typeof(ApiDocumentation).Assembly.GetManifestResourceStream("StockCount.Swagger.zh-cn.js")
                ?? throw new InvalidOperationException("缺少内置 Swagger 中文资源。");
            using var reader = new StreamReader(stream);
            return Results.Text(reader.ReadToEnd(), "application/javascript; charset=utf-8");
        }).ExcludeFromDescription();
    }
}

public sealed class ChineseOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        var path = context.ApiDescription.RelativePath?.Split('?')[0];
        var method = context.ApiDescription.HttpMethod;
        var key = method + " " + path;
        var (tag, title, detail, response, success) = key switch
        {
            "GET health" => ("服务诊断", "检查 API 服务状态", "仅检查进程是否可达，不连接 SQL Server。", (Type?)null, "200"),
            "GET ready" => ("服务诊断", "测试 SQL Server 连接", "执行 SELECT 1，检查当前帐套连接，不代表具备写入权限。", null, "200"),
            "GET api/v1/inventory-counts" => ("盘点单查询", "查询盘点单列表", "支持搜索、分页，pageSize 默认 50，范围 1–100。", typeof(CountPage), "200"),
            "GET api/v1/inventory-counts/{countNumber}" => ("盘点单查询", "查询指定盘点单", "按盘点单号查询单据与可提交状态。", typeof(CountDetails), "200"),
            "GET api/v1/inventory-counts/{countNumber}/items" => ("盘点单查询", "查询盘点明细", "分页读取 XCOUTD 明细，不修改数据。", typeof(CountItemPage), "200"),
            "POST api/v1/locations/resolve" => ("扫码解析", "解析盘点库位", "按盘点单所在站点及仓库核验 STOLOC 库位。只读。", typeof(ResolvedLocation), "200"),
            "POST api/v1/labels/resolve" => ("扫码解析", "解析箱码二维码", "按完整 QR 查询 XLABELDTL 标签、物料信息及本单重复状态。只读。", typeof(ResolvedLabel), "200"),
            "POST api/v1/inventory-counts/{countNumber}/items" => ("盘点提交", "提交实盘明细（实际写入）", "危险操作：实际写入 ERP。数量须人工录入；必须提供非空 UUID 幂等键，重试复用原键。服务器仍校验帐套授权、库位、标签、单位和重复箱码。", typeof(SaveCountItemResult), "201"),
            "GET api/v1/submissions/{requestId}" => ("盘点提交", "查询提交结果", "按盘点单号及原 UUID 查询已提交记录；未找到不代表可以更换 UUID 重试。", typeof(SaveCountItemResult), "200"),
            _ => ("其他", "接口", "", null, "200")
        };
        operation.Tags = new List<OpenApiTag> { new() { Name = tag } };
        operation.Summary = title;
        operation.Description = detail;
        operation.Parameters ??= new List<OpenApiParameter>();

        if (path != "health")
        {
            operation.Parameters.Add(new OpenApiParameter
            {
                Name = "X-StockCount-Environment", In = ParameterLocation.Header, Required = true,
                Description = "帐套：Test = XTEST 测试；Production = COB 正式。",
                Schema = new OpenApiSchema { Type = "string", Default = new OpenApiString("Test"),
                    Enum = new List<IOpenApiAny> { new OpenApiString("Test"), new OpenApiString("Production") } }
            });
            operation.Parameters.Add(new OpenApiParameter
            {
                Name = "X-StockCount-Device-Key", In = ParameterLocation.Header,
                Description = "旧版设备认证密钥；内网免密模式留空，不保存到文档。",
                Schema = new OpenApiSchema { Type = "string" }
            });
        }

        if (success == "201")
            operation.Parameters.Add(new OpenApiParameter
            {
                Name = "Idempotency-Key", In = ParameterLocation.Header, Required = true,
                Description = "本次提交的唯一 UUID；重试使用同一值。", Schema = new OpenApiSchema { Type = "string", Format = "uuid" }
            });

        foreach (var parameter in operation.Parameters)
            parameter.Description = parameter.Name switch
            {
                "countNumber" => "盘点单号，最长 30 字符。",
                "requestId" => "原提交请求 UUID。",
                "pageSize" => "每页记录数，1–100，默认 50。",
                "cursor" => "上一页返回的 nextCursor，首次查询留空。",
                "search" => "搜索文本，最多 100 字符。",
                _ => parameter.Description
            };

        var schema = response is null
            ? new OpenApiSchema { Type = "object", Properties = path == "health"
                ? new Dictionary<string, OpenApiSchema> {
                    ["service"] = new() { Type = "string", Description = "服务名称", Example = new OpenApiString("StockCount.Api") },
                    ["status"] = new() { Type = "string", Description = "运行状态" },
                    ["version"] = new() { Type = "string", Description = "服务版本" },
                    ["utc"] = new() { Type = "string", Format = "date-time", Description = "UTC 时间" } }
                : new Dictionary<string, OpenApiSchema> {
                    ["environment"] = new() { Type = "string", Description = "帐套" },
                    ["database"] = new() { Type = "string", Description = "数据库连接状态", Example = new OpenApiString("Reachable") } } }
            : context.SchemaGenerator.GenerateSchema(response, context.SchemaRepository);

        OpenApiResponse Ok(string description) => new()
        {
            Description = description,
            Content = new Dictionary<string, OpenApiMediaType> { ["application/json"] = new() { Schema = schema } }
        };
        operation.Responses[success] = Ok(success == "201" ? "提交成功，已写入一条明细" : "请求成功");
        if (success == "201") operation.Responses["200"] = Ok("幂等重放，返回已提交结果");
        if (path == "health") return;
        foreach (var (status, description) in new[] { ("400", "参数或帐套不合法"), ("401", "设备密钥未授权"), ("403", "访问或写入被拒绝"), ("404", "未找到对应记录"), ("409", "重复箱码、单据状态或事务冲突"), ("422", "库位、标签或业务数据不合法"), ("503", "数据库未就绪、写入不可用或提交结果未知") })
        {
            if (path == "ready" && status is "404" or "409" or "422") continue;
            operation.Responses[status] = new OpenApiResponse { Description = description };
        }
    }
}

public sealed class ChineseSchemaFilter : ISchemaFilter
{
    private static readonly Dictionary<string, string> Labels = new(StringComparer.OrdinalIgnoreCase)
    {
        ["countNumber"]="盘点单号", ["description"]="盘点名称", ["siteCode"]="站点", ["warehouseCode"]="仓库",
        ["countDate"]="盘点日期", ["statusCode"]="状态编码", ["countType"]="盘点类型", ["canSubmit"]="是否可提交",
        ["cannotSubmitReason"]="不可提交原因", ["environment"]="帐套", ["items"]="记录列表", ["nextCursor"]="下一页游标",
        ["count"]="盘点单", ["rawCode"]="原始库位码", ["locationCode"]="库位", ["isValid"]="是否有效",
        ["message"]="提示信息", ["rawQrCode"]="完整原始二维码", ["boxNumber"]="箱号", ["itemCode"]="物料编码",
        ["itemName"]="品名", ["specification"]="规格", ["lotNumber"]="批号", ["stockUnit"]="存货单位",
        ["labelQty"]="标签数量（不是实盘数量）", ["labelStatusCode"]="标签状态", ["alreadyRecordedInCount"]="本盘点单是否已扫描",
        ["detailId"]="明细标识", ["packageUnit"]="包装单位", ["quantityPcu"]="包装单位数量", ["quantityStu"]="存货单位数量",
        ["createdAt"]="创建时间", ["countedLocation"]="实盘库位", ["countedQtyStu"]="人工录入实盘数量，必填",
        ["savedQtyStu"]="已保存存货单位数量", ["savedQtyPcu"]="已保存包装单位数量", ["requestId"]="幂等请求 UUID",
        ["status"]="提交状态", ["replayed"]="是否为幂等重放"
    };
    public void Apply(OpenApiSchema schema, SchemaFilterContext context)
    {
        foreach (var (name, property) in schema.Properties)
            if (Labels.TryGetValue(name, out var label)) property.Description = label;
    }
}
