namespace DocMan.Models;

public record VolumeInfo(
    string Name,
    string Driver,
    string Mountpoint,
    string Scope,
    string Created,
    bool   Dangling
);
