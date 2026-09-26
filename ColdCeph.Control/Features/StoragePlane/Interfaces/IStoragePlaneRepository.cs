using ColdCeph.Control.Features.StoragePlane.Models;

namespace ColdCeph.Control.Features.StoragePlane.Interfaces;

public interface IStoragePlaneRepository
{
    StoragePlaneRecord Load();
    void Save(StoragePlaneRecord record);
}
