namespace ColdCeph.Control.Features.StoragePlane.Interfaces;

public interface INooutProvider
{
    void SetGroupNoout(string scope);
    void UnsetGroupNoout(string scope);
}
