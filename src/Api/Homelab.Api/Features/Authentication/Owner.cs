
namespace Homelab.Api;
public sealed class Owner
{
    public int Id { get; set; }
    public string Username { get; set; } = "";
    public string PasswordHash { get; set; } = "";
}
