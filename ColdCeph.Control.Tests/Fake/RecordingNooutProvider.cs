using ColdCeph.Control.Features.StoragePlane.Interfaces;

namespace ColdCeph.Control.Tests.Fake;

public sealed class RecordingNooutProvider : INooutProvider
{
    public List<string> Commands { get; } = [];

    public void SetGroupNoout(string scope) => Commands.Add($"osd set-group noout {scope}");

    public void UnsetGroupNoout(string scope) => Commands.Add($"osd unset-group noout {scope}");
}
