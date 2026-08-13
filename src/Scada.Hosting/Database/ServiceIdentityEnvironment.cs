using Scada.Infrastructure.Sqlite.Migrations;

namespace Scada.Hosting.Database;

public static class ServiceIdentityEnvironment
{
    public static ServiceIdentityPolicy FromEnvironment()
    {
        Dictionary<string, ServiceIdentity> identities = new(StringComparer.OrdinalIgnoreCase);
        Add("SCADA_WEB_SERVICE_SID", ServiceIdentity.Web);
        Add("SCADA_RUNTIME_SERVICE_SID", ServiceIdentity.Runtime);
        return new ServiceIdentityPolicy(identities);

        void Add(string variable, ServiceIdentity identity)
        {
            string? sid = Environment.GetEnvironmentVariable(variable);
            if (!string.IsNullOrWhiteSpace(sid)) identities[sid] = identity;
        }
    }
}
