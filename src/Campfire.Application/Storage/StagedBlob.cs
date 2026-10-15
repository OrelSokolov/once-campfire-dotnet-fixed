using Campfire.Application.Storage.Disk;

namespace Campfire.Application.Storage;

/// <summary>
/// A new blob whose bytes are on disk but whose row isn't saved yet: the file half of an upload,
/// variant or preview. The protocol that keeps files and rows consistent:
/// <list type="number">
/// <item>The file is staged (complete, checksummed, flushed) under the disk service's staging directory.</item>
/// <item><see cref="Publish"/> renames it to <c>storage/files/ab/cd/&lt;key&gt;</c> in one step.</item>
/// <item>Once the row that names the key is committed, <see cref="Keep"/> hands the file over to it.</item>
/// </list>
/// Disposing without <see cref="Keep"/> deletes the file, staged or published, so a rollback or a
/// lost race leaves nothing behind. Message uploads publish after their transaction commits, as
/// Rails uploads after commit; variants and previews publish before recording their rows, so a
/// recorded variant always has its file.
/// </summary>
public sealed class StagedBlob : IDisposable
{
    private readonly DiskService _disk;
    private readonly StagedFile _file;
    private bool _kept;

    internal StagedBlob( DiskService disk, NewBlob blob, StagedFile file )
    {
        _disk = disk;
        _file = file;
        Blob = blob;
    }

    /// <summary>The row to insert.</summary>
    public NewBlob Blob { get; }

    public bool IsPublished => _file.IsPublished;

    /// <summary>Renames the staged file to the blob key's path.</summary>
    public void Publish( ) => _disk.Publish( _file, Blob.Key );

    /// <summary>The row naming this key is committed: the published file now belongs to it.</summary>
    public void Keep( )
    {
        if ( !_file.IsPublished )
        {
            throw new InvalidOperationException( "Publish the file before keeping it." );
        }

        _kept = true;
    }

    public void Dispose( )
    {
        if ( _kept )
        {
            return;
        }

        if ( _file.IsPublished )
        {
            _disk.Delete( Blob.Key );
        }
        else
        {
            _file.Dispose( );
        }
    }
}
