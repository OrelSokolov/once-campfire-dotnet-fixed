using Campfire.Application.Memberships;
using Campfire.Application.Messages;
using Campfire.Application.Rooms;
using Campfire.Application.Sessions;
using Campfire.Application.Tests.Support;
using Campfire.Application.Users;

namespace Campfire.Application.Tests.Fixtures;

/// <summary>The fixture loader against facts from a Ruby-loaded fixtures database (Rust <c>fixtures_test.rs</c>).</summary>
public sealed class FixtureLoaderTests
{
    [Fact]
    public void Identify_matches_rails( )
    {
        // ActiveRecord::FixtureSet.identify, from the reference app.
        Assert.Equal( 127326141, RailsFixtures.Identify( "david" ) );
        Assert.Equal( 873240054, RailsFixtures.Identify( "signal" ) );
        Assert.Equal( 654632876, RailsFixtures.Identify( "designers" ) );
    }

    [Fact]
    public void Table_names_follow_the_fixture_paths( )
    {
        var directory = Path.Combine( "f" );

        Assert.Equal( "action_text_rich_texts", RailsFixtures.TableName( directory, Path.Combine( "f", "action_text", "rich_texts.yml" ) ) );
        Assert.Equal( "push_subscriptions", RailsFixtures.TableName( directory, Path.Combine( "f", "push", "subscriptions.yml" ) ) );
        Assert.Equal( "users", RailsFixtures.TableName( directory, Path.Combine( "f", "users.yml" ) ) );
    }

    [Fact]
    public void Erb_ago_renders_whole_seconds_in_utc( )
    {
        var now = new DateTime( 2026, 9, 26, 12, 24, 38, 211, 309, DateTimeKind.Utc );
        var erb = new FixtureErb( now, Passwords.MinimumIterations );

        Assert.Equal( "created_at: 2026-09-26 11:24:38 UTC", erb.Render( "created_at: <%= 1.hour.ago %>", "x.yml" ) );
        Assert.Equal( "\na: 2026-09-26 12:19:38 UTC", erb.Render( "<% x = 5.minutes.ago %>\na: <%= x %>", "x.yml" ) );
        Assert.Throws<InvalidDataException>( ( ) => erb.Render( "<%= Time.now %>", "x.yml" ) );
    }

    [Fact]
    public async Task Ids_are_label_crcs( )
    {
        using var testDatabase = await TestDatabase.CreateAsync( );

        Assert.Equal( "David", await testDatabase.ReadAsync( db => UserQueries.Find( db, 127326141 ).Name ) );
        Assert.Equal( "Designers", await testDatabase.ReadAsync( db => RoomQueries.FindById( db, 654632876 )!.Name ) );
        Assert.Equal( 13, await testDatabase.CountAsync( "messages" ) );
    }

    [Fact]
    public async Task Associations_enums_and_column_defaults( )
    {
        using var testDatabase = await TestDatabase.CreateAsync( );

        var pets = await testDatabase.ReadAsync( db => RoomQueries.FindById( db, TestDatabase.Id( "pets" ) )! );
        var bender = await testDatabase.ReadAsync( db => UserQueries.Find( db, TestDatabase.Id( "bender" ) ) );
        var kevinDesigners = await testDatabase.ReadAsync( db => MembershipQueries.Find( db, TestDatabase.Id( "kevin_designers" ) ) );
        var richText = await testDatabase.ReadAsync( db =>
            Sql.Column( db, "SELECT record_id || ' ' || record_type FROM action_text_rich_texts WHERE id = @p0", TestDatabase.Id( "first" ) ).Single( ) );

        Assert.Equal( (RoomType.Open, TestDatabase.Id( "david" )), (pets.Type, pets.CreatorId) );
        Assert.Equal( UserRole.Administrator, (await testDatabase.ReadAsync( db => UserQueries.Find( db, TestDatabase.Id( "david" ) ) )).Role );
        Assert.Equal( UserRole.Member, (await testDatabase.ReadAsync( db => UserQueries.Find( db, TestDatabase.Id( "jz" ) ) )).Role );
        Assert.Equal( (UserRole.Bot, 12), (bender.Role, bender.BotToken!.Length) );
        Assert.Equal( Involvement.Mentions, kevinDesigners.Involvement );
        Assert.Equal( 0, kevinDesigners.Connections );
        Assert.Equal( $"{TestDatabase.Id( "first" )} Message", richText );
    }

    [Fact]
    public async Task Erb_times_are_whole_seconds_and_default_timestamps_are_now( )
    {
        using var testDatabase = await TestDatabase.CreateAsync( );

        var first = await testDatabase.ReadAsync( db => MessageQueries.Find( db, TestDatabase.Id( "first" ) ) );
        var sessions = await testDatabase.ReadAsync( db => SessionOperations.ForUser( db, TestDatabase.Id( "david" ) ) );
        var session = sessions.Single( userSession => userSession.Id == TestDatabase.Id( "david_safari" ) );

        Assert.Equal( 0, first.CreatedAt.Ticks % TimeSpan.TicksPerSecond );
        Assert.True( first.UpdatedAt > first.CreatedAt );
        Assert.Equal( 0, session.LastActiveAt.Ticks % TimeSpan.TicksPerSecond );
        Assert.InRange( (session.CreatedAt - session.LastActiveAt).TotalSeconds, 7199, 7201 );
    }

    [Fact]
    public async Task Passwords_use_identity_v3_and_verify_secret123456( )
    {
        using var testDatabase = await TestDatabase.CreateAsync( );

        var david = await testDatabase.ReadAsync( db => UserQueries.Find( db, TestDatabase.Id( "david" ) ) );

        Assert.True( Passwords.Verify( "secret123456", david.PasswordDigest ) );
        Assert.Equal( ( byte ) 0x01, Convert.FromBase64String( david.PasswordDigest! )[0] );
    }

    [Fact]
    public void Unsupported_yaml_fails_loudly( )
    {
        Assert.Throws<InvalidDataException>( ( ) => FixtureYaml.Parse( "a:\n  b: [1, 2]\n", "x.yml" ) );
        Assert.Throws<InvalidDataException>( ( ) => FixtureYaml.Parse( "a: 1\n", "x.yml" ) );
    }

    [Fact]
    public void Yaml_scalars_keep_quoting_and_psych_types( )
    {
        var rows = FixtureYaml.Parse( "one:\n  id: \"0001\"\n  plain: 0001\n  n: 42\n  on: yes\n  s: 'it''s' # note\n  sym: :david\n", "x.yml" );
        var row = rows.Single( ).Row.ToDictionary( entry => entry.Key, entry => entry.Value );

        Assert.Equal( new YamlScalar( "0001", Quoted: true ), row["id"] );
        Assert.Null( row["plain"].Integer );
        Assert.Equal( 42, row["n"].Integer );
        Assert.True( row["on"].Boolean );
        Assert.Equal( "it's", row["s"].Text );
        Assert.Equal( ":david", row["sym"].Text );
    }
}
