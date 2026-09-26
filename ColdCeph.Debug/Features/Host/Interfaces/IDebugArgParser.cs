using ColdCeph.Debug.Features.Host.DTOs;

namespace ColdCeph.Debug.Features.Host.Interfaces;

public interface IDebugArgParser
{
    DebugCommandDto Parse(string[] args);
}
