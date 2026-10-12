namespace ExtraSpace.Api.Platform.Hosting;

/// <summary>Extra endpoints a host adds after the features (the test host's <c>/test/*</c>). Production registers none.</summary>
public interface IEndpointMapper
{
    void Map(IEndpointRouteBuilder endpoints);
}
