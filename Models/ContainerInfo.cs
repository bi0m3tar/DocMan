namespace DocMan.Models;

public class ContainerInfo
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Project { get; set; } = string.Empty;
    public string Service { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Health { get; set; } = "none";
    public string Image { get; set; } = string.Empty;
    public string Ports { get; set; } = string.Empty;
    public bool IsRunning { get; set; }
    public bool IsStandalone { get; set; }
    public string? ComposeFile { get; set; }
    public string? WorkingDir { get; set; }
}
