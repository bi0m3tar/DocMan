namespace DocMan.Models;

public class DisplayRow
{
    public bool IsProjectRow { get; set; }
    public bool IsStandalone { get; set; }
    public string Project { get; set; } = string.Empty;
    public ContainerInfo? Container { get; set; }
    public List<ContainerInfo> ProjectContainers { get; set; } = new();
}
