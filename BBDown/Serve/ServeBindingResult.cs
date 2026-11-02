using System;
using System.Data;
using System.Net.Http.Json;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Http;

namespace BBDown.Serve;

internal record struct ServeBindingResult<T>(T? Result, Exception? Exception)
{
    public readonly bool IsValid => Exception is null;

    public static async ValueTask<ServeBindingResult<T>> BindAsync(HttpContext httpContext)
    {
        try
        {
            var jsonTypeInfo = ServeRequestOptionsJsonContext.Default.GetTypeInfo(typeof(T));
            if (jsonTypeInfo is null)
            {
                return new(default, new InvalidOperationException($"Cannot find TypeInfo for type {typeof(T)}"));
            }

            var item = await httpContext.Request.ReadFromJsonAsync(jsonTypeInfo);

            if (item is null)
            {
                return new(default, new NoNullAllowedException( ));
            }

            return new((T) item, null);
        }
        catch (Exception ex)
        {
            return new(default, ex);
        }
    }
}
