using System.Diagnostics.CodeAnalysis;
using API.Schema.ActionsContext;
using API.Schema.ActionsContext.Actions;

namespace API.Workers;

public class MoveFileOrFolderWorker(string toLocation, string fromLocation, IEnumerable<BaseWorker>? dependsOn = null)
    : BaseWorkerWithContexts(dependsOn)
{
    public readonly string FromLocation = fromLocation;
    public readonly string ToLocation = toLocation;
    
    [SuppressMessage("ReSharper", "InconsistentNaming")]
    private ActionsContext ActionsContext = null!;

    protected override void SetContexts(IServiceScope serviceScope)
    {
        ActionsContext = GetContext<ActionsContext>(serviceScope);
    }

    protected override async Task<BaseWorker[]> DoWorkInternal()
    {
        try
        {
            if (Directory.Exists(FromLocation))
            {
                if (Directory.Exists(ToLocation) || File.Exists(ToLocation))
                {
                    Log.ErrorFormat("Folder already exists at {0}", ToLocation);
                    return [];
                }
                Directory.Move(FromLocation, ToLocation);
            }
            else if (File.Exists(FromLocation))
            {
                if (File.Exists(ToLocation) || Directory.Exists(ToLocation))
                {
                    Log.ErrorFormat("File already exists at {0}", ToLocation);
                    return [];
                }
                File.Move(FromLocation, ToLocation);
            }
            else
            {
                Log.ErrorFormat("Nothing to move at {0}", FromLocation);
                return [];
            }
        }
        catch (Exception e)
        {
            Log.Error(e);
        }

        ActionsContext.Actions.Add(new DataMovedActionRecord(FromLocation, ToLocation));
        if(await ActionsContext.Sync(CancellationToken, GetType(), "Library Moved") is { success: false } actionsContextException)
            Log.ErrorFormat("Failed to save database changes: {0}", actionsContextException.exceptionMessage);

        return [];
    }

    public override string ToString() => $"{base.ToString()} {FromLocation} {ToLocation}";
}