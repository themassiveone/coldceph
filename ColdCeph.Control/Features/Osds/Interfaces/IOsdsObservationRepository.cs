using ColdCeph.Core.Features.Osds.DTOs;

namespace ColdCeph.Control.Features.Osds.Interfaces;

public interface IOsdsObservationRepository
{
    IReadOnlyList<OsdDto> LoadOsds();
    IReadOnlyDictionary<string, string> LoadErrors();
    void Save(IReadOnlyList<OsdDto> osds, IReadOnlyDictionary<string, string> errors);
}
