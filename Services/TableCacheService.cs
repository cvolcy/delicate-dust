using System;

namespace Cvolcy.DelicateDust.Services
{
    internal class TableCacheService
    {
        public T GetOrCreate<T>(string key, Func<T> createFunc)
        {
            return createFunc();
        }
    }

    public interface ITableCache
    {

    }
}
