namespace ClipboardManager;

/// <summary>剪贴板面板批量粘贴队列：关闭 / 先进先出 / 后进先出（新复制自动入队）。</summary>
public enum BatchPasteQueueMode
{
    Off,
    Fifo,
    Lifo
}
