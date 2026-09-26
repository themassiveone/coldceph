namespace ColdCeph.Control.Features.S3.Interfaces;

public interface IRgwProxy
{
    Task ProxyAsync(HttpContext context);
}
