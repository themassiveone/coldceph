using ColdCeph.E2E.Tests.Support;
using Xcepto.Builder;
using Xcepto.Data;
using Xcepto.Scenarios;

namespace ColdCeph.E2E.Tests.Scenarios;

public sealed class ColdCephScenario : XceptoScenario
{
    public Uri ControlAddress => SharedEnvironment.ControlAddress;
    public Uri S3Address => SharedEnvironment.S3Address;
    public Uri NodeAddress => SharedEnvironment.NodeAddress;
    public string CephContainer => SharedEnvironment.CephContainer;
    public string RepositoryRoot => SharedEnvironment.RepositoryRoot;
    public string OperatorPassword => SharedEnvironment.OperatorPassword;
    public string NodeToken => SharedEnvironment.NodeToken;
    public string HostId => SharedEnvironment.HostId;
    public string DemoBucket => SharedEnvironment.DemoBucket;

    protected override ScenarioSetup Setup(ScenarioSetupBuilder builder) => builder.Build();
}
