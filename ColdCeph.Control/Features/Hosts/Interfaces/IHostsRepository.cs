using ColdCeph.Control.Features.Hosts.Models;

namespace ColdCeph.Control.Features.Hosts.Interfaces;

public interface IHostsRepository
{
    HostsRecord Load();
    void Save(HostsRecord record);
}
