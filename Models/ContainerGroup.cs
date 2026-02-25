namespace DocMan.Models;

public class ContainerGroup
{
    public string ProjectName { get; set; } = string.Empty;
    public List<ContainerInfo> Containers { get; set; } = new();
}
