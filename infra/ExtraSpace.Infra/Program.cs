using Amazon.CDK;
using ExtraSpace.Infra;

var app = new App();
InfraComposer.Compose(app, System.Environment.GetEnvironmentVariable("CDK_DEFAULT_ACCOUNT"));
app.Synth();
