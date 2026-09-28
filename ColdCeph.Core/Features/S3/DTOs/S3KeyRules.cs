namespace ColdCeph.Core.Features.S3.DTOs;

public static class S3KeyRules
{
    public static string FileName(string key)
    {
        var relative = key.TrimEnd('/');
        var slash = relative.LastIndexOf('/');
        return slash < 0 ? relative : relative[(slash + 1)..];
    }
}
