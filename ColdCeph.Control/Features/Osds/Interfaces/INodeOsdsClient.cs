using ColdCeph.Core.Features.Osds.DTOs;

namespace ColdCeph.Control.Features.Osds.Interfaces;

public interface INodeOsdsClient
{
    OsdMutationResult Start(Uri endpoint, OsdMutationRequest request);
    OsdMutationResult Stop(Uri endpoint, OsdMutationRequest request);
}
