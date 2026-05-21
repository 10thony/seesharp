namespace TestNativeMobileBackendApi.Configuration;

public class DockerOptions
{
    public const string SectionName = "Docker";

    public bool Enabled { get; set; } = true;
    public string ComposeFile { get; set; } = "docker-compose.yml";
    public string ContainerName { get; set; } = "testnativemobile-postgres";
    public int StartupTimeoutSeconds { get; set; } = 120;
}
