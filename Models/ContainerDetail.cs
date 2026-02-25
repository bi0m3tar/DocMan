namespace DocMan.Models;

public record ContainerDetail(
    string Id,
    string Name,
    string Image,
    string Created,
    string Status,
    string? MemoryLimit,
    string? CpuLimit,
    IReadOnlyList<MountInfo> Mounts,
    IReadOnlyList<NetworkInfo> Networks,
    IReadOnlyList<string> Ports
);

public record MountInfo(string Source, string Destination, string Mode);

public record NetworkInfo(string Name, string IpAddress, string Gateway);

public record ContainerStats(
    string CpuPercent,
    string MemUsage,
    string MemLimit,
    string MemPercent,
    string NetIn,
    string NetOut,
    string BlockIn,
    string BlockOut,
    string Pids
);
