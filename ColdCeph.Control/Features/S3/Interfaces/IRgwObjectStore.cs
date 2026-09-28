using ColdCeph.Core.Features.S3.DTOs;

namespace ColdCeph.Control.Features.S3.Interfaces;

public interface IRgwObjectStore
{
    IReadOnlyList<S3BucketDto> ListBuckets();
    S3ObjectListingDto ListObjects(string bucket, string prefix);
    Stream OpenObject(string bucket, string key);
    void PutObject(string bucket, string key, Stream body, string contentType);
}
