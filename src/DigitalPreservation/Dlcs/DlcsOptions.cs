namespace Dlcs;

public class DlcsOptions
{
    public string? ApiKey { get; set; }
    public string? ApiSecret { get; set; }
    public int CustomerId { get; set; }
    public string? CustomerName { get; set; }
    public int CustomerDefaultSpace { get; set; }
    public string? ApiEntryPoint { get; set; }
    public int BatchSize { get; set; } = 100;
    public int DefaultTimeoutMs { get; set; } = 10000;
}