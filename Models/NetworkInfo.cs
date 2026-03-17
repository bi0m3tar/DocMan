namespace DocMan.Models;

public record DockerNetworkInfo(
    string Id,
    string Name,
    string Driver,
    string Scope,
    string Subnet,
    string Gateway,
    bool   Internal,
    int    ContainerCount,
    string Created
);
