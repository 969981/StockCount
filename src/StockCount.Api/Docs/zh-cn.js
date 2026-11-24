// Local, offline Chinese labels. Never translate payloads, JSON or identifiers.
(() => {
  document.documentElement.lang = "zh-CN";
  const labels = new Map(Object.entries({
    "Explore":"查看文档", "Authorize":"认证", "Available authorizations":"可用认证方式",
    "Close":"关闭", "Logout":"退出认证", "Parameters":"请求参数", "No parameters":"无请求参数",
    "Try it out":"在线测试", "Cancel":"取消", "Execute":"执行请求", "Clear":"清空",
    "Responses":"响应结果", "Server response":"服务器响应", "Response body":"响应正文",
    "Response headers":"响应头", "Request URL":"请求地址", "Request body":"请求正文",
    "Curl":"curl 命令", "Code":"状态码", "Details":"详情", "Description":"说明",
    "Links":"关联", "No links":"无关联", "Example Value":"示例值", "Schema":"数据结构",
    "Schemas":"数据模型", "Model":"数据模型", "Models":"数据模型", "Name":"参数名",
    "Download":"下载", "Copy":"复制", "Required":"必填", "required":"必填",
    "Media type":"媒体类型", "Response content type":"响应内容类型",
    "Request duration":"请求耗时", "Loading...":"加载中…", "Servers":"服务器",
    "Select a definition":"选择接口版本", "Filter by tag":"按接口分组筛选",
    "Expand operation":"展开接口", "Collapse operation":"收起接口"
  }));
  let pending = false;
  const translate = () => {
    pending = false;
    const root = document.querySelector(".swagger-ui");
    if (!root) return;
    const walker = document.createTreeWalker(root, NodeFilter.SHOW_TEXT);
    while (walker.nextNode()) {
      const node = walker.currentNode;
      const parent = node.parentElement;
      if (!parent || parent.closest("pre,code,textarea,input,select,.highlight-code,.microlight,.renderedMarkdown,.model,.parameter__name,.opblock-summary-path")) continue;
      const original = node.nodeValue;
      const replacement = labels.get(original.trim());
      if (replacement) node.nodeValue = original.replace(original.trim(), replacement);
    }
    for (const el of root.querySelectorAll("[title],[aria-label],[placeholder]")) {
      for (const attr of ["title","aria-label","placeholder"]) {
        const value = el.getAttribute(attr);
        if (labels.has(value)) el.setAttribute(attr, labels.get(value));
      }
    }
  };
  const schedule = () => {
    if (pending) return;
    pending = true;
    requestAnimationFrame(translate);
  };
  new MutationObserver(schedule).observe(document.body, { childList:true, subtree:true, characterData:true });
  schedule();
})();
