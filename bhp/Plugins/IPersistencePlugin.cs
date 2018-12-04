using Bhp.Persistence;

namespace Bhp.Plugins
{
    public interface IPersistencePlugin
    {
        void OnPersist(Snapshot snapshot);
    }
}
