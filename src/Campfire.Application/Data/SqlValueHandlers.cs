using System.Data;
using System.Data.Common;
using System.Globalization;

using Campfire.Application.Data;
using Campfire.Application.Memberships;
using Campfire.Application.Rooms;
using Campfire.Application.Users;

using Dapper;

// Every Dapper call site in this assembly is replaced at build time by Dapper.AOT's generated interceptors: parameter
// binders and row readers written as plain C#, with no reflection or Reflection.Emit, so they run under Native AOT.
[module: DapperAot]

// How the schema's typed columns cross the boundary, for parameters and results alike (docs/development.md).
[module: TypeHandler( typeof( DateTime ), typeof( RailsTimestampHandler ) )]
[module: TypeHandler( typeof( UserRole ), typeof( UserRoleHandler ) )]
[module: TypeHandler( typeof( UserStatus ), typeof( UserStatusHandler ) )]
[module: TypeHandler( typeof( RoomType ), typeof( RoomTypeHandler ) )]
[module: TypeHandler( typeof( Involvement ), typeof( InvolvementHandler ) )]

namespace Campfire.Application.Data;

/// <summary>
/// Every <c>datetime</c> column and every <see cref="DateTime"/> parameter, in Rails' text form:
/// written with <see cref="RailsTimestamp.Format(DateTime)"/>, read with <see cref="RailsTimestamp.Parse"/>. SQLite
/// compares these values as text, so a parameter in any other form would misorder or miss rows.
/// </summary>
internal sealed class RailsTimestampHandler : DbValueHandler<DateTime>
{
    protected override void Configure( DbParameter parameter ) => parameter.DbType = DbType.String;

    protected override void SetValueCore( DbParameter parameter, DateTime value ) => parameter.Value = RailsTimestamp.Format( value );

    public override DateTime Parse( DbDataReader reader, int ordinal, int token ) => RailsTimestamp.Parse( reader.GetString( ordinal ) );

    protected override DateTime Parse( object? value ) => RailsTimestamp.Parse( Convert.ToString( value, CultureInfo.InvariantCulture ) ?? "" );
}

/// <summary><c>users.role</c>, an integer enum (<c>enum :role, %i[ member administrator bot ]</c>).</summary>
internal sealed class UserRoleHandler : DbValueHandler<UserRole>
{
    protected override void Configure( DbParameter parameter ) => parameter.DbType = DbType.Int64;

    protected override void SetValueCore( DbParameter parameter, UserRole value ) => parameter.Value = ( long ) value;

    public override UserRole Parse( DbDataReader reader, int ordinal, int token ) => StoredEnums.Role( reader.GetInt64( ordinal ) );

    protected override UserRole Parse( object? value ) => StoredEnums.Role( Convert.ToInt64( value, CultureInfo.InvariantCulture ) );
}

/// <summary><c>users.status</c>, an integer enum (<c>enum :status, %i[ active deactivated banned ]</c>).</summary>
internal sealed class UserStatusHandler : DbValueHandler<UserStatus>
{
    protected override void Configure( DbParameter parameter ) => parameter.DbType = DbType.Int64;

    protected override void SetValueCore( DbParameter parameter, UserStatus value ) => parameter.Value = ( long ) value;

    public override UserStatus Parse( DbDataReader reader, int ordinal, int token ) => StoredEnums.Status( reader.GetInt64( ordinal ) );

    protected override UserStatus Parse( object? value ) => StoredEnums.Status( Convert.ToInt64( value, CultureInfo.InvariantCulture ) );
}

/// <summary><c>rooms.type</c>, the STI class name (<see cref="RoomTypes"/>).</summary>
internal sealed class RoomTypeHandler : DbValueHandler<RoomType>
{
    protected override void Configure( DbParameter parameter ) => parameter.DbType = DbType.String;

    protected override void SetValueCore( DbParameter parameter, RoomType value ) => parameter.Value = RoomTypes.ClassName( value );

    public override RoomType Parse( DbDataReader reader, int ordinal, int token ) => RoomTypes.FromClassName( reader.GetString( ordinal ) );

    protected override RoomType Parse( object? value ) => RoomTypes.FromClassName( Convert.ToString( value, CultureInfo.InvariantCulture ) ?? "" );
}

/// <summary><c>memberships.involvement</c>, a string enum stored by name (<see cref="Involvements"/>).</summary>
internal sealed class InvolvementHandler : DbValueHandler<Involvement>
{
    protected override void Configure( DbParameter parameter ) => parameter.DbType = DbType.String;

    protected override void SetValueCore( DbParameter parameter, Involvement value ) => parameter.Value = Involvements.Name( value );

    public override Involvement Parse( DbDataReader reader, int ordinal, int token ) => Involvements.Parse( reader.GetString( ordinal ) );

    protected override Involvement Parse( object? value ) => Involvements.Parse( Convert.ToString( value, CultureInfo.InvariantCulture ) ?? "" );
}

/// <summary>Reads the integer enums, rejecting values Rails would not map.</summary>
internal static class StoredEnums
{
    public static UserRole Role( long value )
    {
        if ( !Enum.IsDefined( ( UserRole ) value ) )
        {
            throw new InvalidOperationException( $"Unknown users.role {value}" );
        }

        return ( UserRole ) value;
    }

    public static UserStatus Status( long value )
    {
        if ( !Enum.IsDefined( ( UserStatus ) value ) )
        {
            throw new InvalidOperationException( $"Unknown users.status {value}" );
        }

        return ( UserStatus ) value;
    }
}
