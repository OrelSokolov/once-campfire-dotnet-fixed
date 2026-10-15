namespace Campfire.Application.Integrations.QrCodes;

/// <summary>
/// <c>RQRCodeCore::QRCodeRunTimeError</c>: the data doesn't fit a version 40 symbol at level H. Rails lets it raise, so
/// <c>QrCodeController#show</c> answers 500.
/// </summary>
public sealed class QrCodeCapacityException : Exception
{
    public QrCodeCapacityException( ) : base( "Data length exceed maximum capacity of version 40" )
    {
    }

    public QrCodeCapacityException( string message ) : base( message )
    {
    }

    public QrCodeCapacityException( string message, Exception innerException ) : base( message, innerException )
    {
    }
}
