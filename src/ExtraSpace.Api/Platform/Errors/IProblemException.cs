namespace ExtraSpace.Api.Platform.Errors;

/// <summary>An exception from an outside system that knows its own HTTP answer (S1: SiteLink; S1-4: Stripe).</summary>
public interface IProblemException
{
    int Status { get; }
    string Code { get; }
    string Title { get; }
}
