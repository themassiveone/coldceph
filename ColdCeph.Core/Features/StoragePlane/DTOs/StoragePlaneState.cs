namespace ColdCeph.Core.Features.StoragePlane.DTOs;

public enum StoragePlaneState
{
    Cold,
    Waking,
    Ready,
    Quiescing,
    Sleeping,
    Faulted
}
