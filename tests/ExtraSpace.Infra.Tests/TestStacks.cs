using Amazon.CDK;
using Amazon.CDK.Assertions;

namespace ExtraSpace.Infra.Tests;

internal static class TestStacks
{
    private const string FakeAccount = "123456789012";
    private static readonly object Gate = new();
    private static IReadOnlyDictionary<string, Stack>? _stacks;
    private static readonly Dictionary<string, Template> Templates = new();

    public static Stack Stack(string stackName)
    {
        lock (Gate)
        {
            _stacks ??= InfraComposer.Compose(new App(), FakeAccount, TestInputs.For);
            return _stacks[stackName];
        }
    }

    public static Template For(string stackName)
    {
        lock (Gate)
        {
            if (!Templates.TryGetValue(stackName, out var template))
            {
                template = Template.FromStack(Stack(stackName));
                Templates[stackName] = template;
            }
            return template;
        }
    }
}
