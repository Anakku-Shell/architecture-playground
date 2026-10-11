using System.Net;
using System.Text.Json;
using Xunit;

namespace Shop.ContractTests;

/// <summary>
/// Every error of the API is a ProblemDetails document (RFC 9457): content type
/// <c>application/problem+json</c> and a JSON body with <c>status</c>; validation errors add <c>errors</c>.
/// </summary>
public static class ProblemAssert
{
    public static async Task IsProblem(HttpResponseMessage response, HttpStatusCode expected)
    {
        ArgumentNullException.ThrowIfNull(response);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.True(response.StatusCode == expected, $"Expected {(int)expected} but got {(int)response.StatusCode}: {body}");
        Assert.True(
            response.Content.Headers.ContentType?.MediaType == "application/problem+json",
            $"Expected application/problem+json but got '{response.Content.Headers.ContentType?.MediaType}': {body}");

        using var json = Parse(body);
        Assert.Equal((int)expected, json.RootElement.GetProperty("status").GetInt32());
    }

    /// <summary>
    /// A <c>400</c> with an <c>errors</c> member (ASP.NET Core's ValidationProblem shape). Every validation
    /// rule of the contract, including "same product twice in an order", answers this way. When
    /// <paramref name="fields"/> are given, each must have its own entry in <c>errors</c>.
    /// </summary>
    public static async Task IsValidationProblem(HttpResponseMessage response, params string[] fields)
    {
        await IsProblem(response, HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using var json = Parse(body);
        Assert.True(json.RootElement.TryGetProperty("errors", out var errors), $"Expected an 'errors' property: {body}");
        foreach (var field in fields)
        {
            Assert.True(errors.TryGetProperty(field, out _), $"Expected an error for '{field}': {body}");
        }
    }

    private static JsonDocument Parse(string body)
    {
        try
        {
            return JsonDocument.Parse(body);
        }
        catch (JsonException)
        {
            Assert.Fail($"Expected a JSON ProblemDetails body but got: '{body}'");
            throw;
        }
    }
}
