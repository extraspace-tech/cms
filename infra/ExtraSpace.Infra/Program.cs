using Amazon.CDK;
using ExtraSpace.Infra;

var app = new App();
var inputsDirectory = System.Environment.GetEnvironmentVariable("ES_DEPLOY_INPUTS");
InfraComposer.Compose(app, System.Environment.GetEnvironmentVariable("CDK_DEFAULT_ACCOUNT"), env => DeployInputs.Load(inputsDirectory, env));
app.Synth();
