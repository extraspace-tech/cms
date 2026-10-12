namespace ExtraSpace.Api.Platform.Logging;

public static class RequestLog
{
    internal const string ItemKey = "es.requestLog";

    /// <summary>
    /// Adds a property to every later log line of this request, including the exception line and the summary line
    /// (for example <c>customerId</c> after sign-in). <see cref="RequestContextEnricher"/> writes it.
    /// </summary>
    public static void Add(HttpContext http, string name, object value) => Properties(http)[name] = value;

    internal static Dictionary<string, object> Properties(HttpContext http)
    {
        if (http.Items[ItemKey] is not Dictionary<string, object> properties)
            http.Items[ItemKey] = properties = new();
        return properties;
    }
}
