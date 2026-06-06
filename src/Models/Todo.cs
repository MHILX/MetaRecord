using System.ComponentModel.DataAnnotations.Schema;

namespace MetaRecord.Models;

[Table("Todos")]
public class Todo : ActiveRecord<Todo>
{
    private string _title = "";
    private string _description = "";
    private string _status = "Open";
    private int _priority = 3;

    public string Title
    {
        get => _title;
        set { _title = value; MarkDirty(); }
    }

    public string Description
    {
        get => _description;
        set { _description = value; MarkDirty(); }
    }

    public string Status
    {
        get => _status;
        set { _status = value; MarkDirty(); }
    }

    public int Priority
    {
        get => _priority;
        set { _priority = value; MarkDirty(); }
    }
}