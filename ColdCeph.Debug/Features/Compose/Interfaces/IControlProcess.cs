namespace ColdCeph.Debug.Features.Compose.Interfaces;

public interface IControlProcess
{
    bool Owned { get; }
    void Start();
    void Stop();
}
