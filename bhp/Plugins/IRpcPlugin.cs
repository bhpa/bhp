using Microsoft.AspNetCore.Http;
using Bhp.IO.Json;

namespace Bhp.Plugins
{
    public interface IRpcPlugin
    {
        void PreProcess(HttpContext context, string method, JArray _params);
        JObject OnProcess(HttpContext context, string method, JArray _params);
        void PostProcess(HttpContext context, string method, JArray _params, JObject result);
    }
}
