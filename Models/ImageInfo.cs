namespace DocMan.Models;

public record ImageInfo(
    string Id,
    string Repository,
    string Tag,
    string Size,
    string Created,
    bool   InUse
);
