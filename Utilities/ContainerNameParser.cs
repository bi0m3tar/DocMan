using System.Text.RegularExpressions;

namespace DocMan.Utilities;

public static class ContainerNameParser
{
    public static (string Project, string Service) Parse(string containerName)
    {
        // Try to extract project and service from container name
        // Docker Compose naming: project-service-instance
        
        // Pattern: project-service-number (e.g., worktasks-service-dev-db-1)
        var matchWithNumber = Regex.Match(containerName, @"^(.+?)-([^-]+)-(\d+)$");
        if (matchWithNumber.Success)
        {
            var project = matchWithNumber.Groups[1].Value;
            var service = matchWithNumber.Groups[2].Value;
            var instance = matchWithNumber.Groups[3].Value;
            return (project, $"{service}-{instance}");
        }
        
        // Pattern: project-service (no trailing number)
        var matchNoNumber = Regex.Match(containerName, @"^(.+?)-([^-]+)$");
        if (matchNoNumber.Success)
        {
            return (matchNoNumber.Groups[1].Value, matchNoNumber.Groups[2].Value);
        }
        
        // Fallback: use full name as both project and service
        return (containerName, containerName);
    }
}
