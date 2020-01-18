using Microsoft.AspNetCore.Http;
using Bhp.IO.Json;

namespace Bhp.Plugins
{
    public interface IRpcPlugin
    {
        JObject OnProcess(HttpContext context, string method, JArray _params);
    }
}
