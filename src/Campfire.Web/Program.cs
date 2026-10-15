using Campfire.Web.App;
using Campfire.Web.Assets;
using Campfire.Web.Cable;
using Campfire.Web.Endpoints;
using Campfire.Web.Hosting;

var prepareFixture = args is ["prepare-fixture"];
var builder = WebApplication.CreateSlimBuilder( prepareFixture ? [] : args );
builder.AddCampfireWeb( );
builder.AddCampfireApplication( );
var app = builder.Build( );
if ( prepareFixture )
{
    SyntheticFixturePreparation.Run( app.Services );
    await app.DisposeAsync( );
    return;
}

await app.BootCampfireAsync( );
app.UseCampfireWeb( staticFiles: pipeline => pipeline.UseCampfireAssets( ) );
app.MapCampfireCable( );
app.MapCampfireEndpoints( );
await app.RunAsync( );

public partial class Program;
