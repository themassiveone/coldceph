using ColdCeph.Control.Features.Integrity.Interfaces;
using ColdCeph.Control.Tests.Support;
using ColdCeph.Core.Features.Integrity.DTOs;

namespace ColdCeph.Control.Tests.Fake;

/// <summary>
/// Stands in for the Ceph monitor, not for ColdCeph's reading of it.
/// <para>
/// It hands back a whole <see cref="CephObservation"/>, which tests take from the fixture
/// corpus via <see cref="CephFixture.Observation"/> — so the health text, the PG states and
/// the derived signals stay consistent with each other the way a real cluster's do. The
/// previous fake exposed each signal as its own settable bool, which let tests describe
/// clusters Ceph cannot produce (recovery in flight, yet HEALTH_OK with no checks) while
/// never exercising the coupling the provider actually implements.
/// </para>
/// </summary>
public sealed class FakeCephQueryProvider : ICephQueryProvider
{
    public CephObservation Observation { get; set; } = CephFixture.Observation(CephFixture.Healthy);

    public int ObservationCalls { get; private set; }

    public int MembershipCalls { get; private set; }

    public bool ThrowOnObservation { get; set; }

    public bool ThrowOnMembership { get; set; }

    public IReadOnlyDictionary<int, OsdMembershipDto> OsdMembership { get; set; } =
        new Dictionary<int, OsdMembershipDto>();

    /// <summary>Loads a scenario from the fixture corpus through the real parser.</summary>
    public FakeCephQueryProvider Seeing(string scenario)
    {
        Observation = CephFixture.Observation(scenario);
        return this;
    }

    public CephObservation GetObservation()
    {
        if (ThrowOnObservation)
            throw new InvalidOperationException("ceph unavailable");
        ObservationCalls++;
        return Observation;
    }

    public IReadOnlyDictionary<int, OsdMembershipDto> ListOsdMembership()
    {
        if (ThrowOnMembership)
            throw new InvalidOperationException("ceph unavailable");
        MembershipCalls++;
        return OsdMembership;
    }
}
