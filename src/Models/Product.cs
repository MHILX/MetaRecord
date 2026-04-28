namespace MetaRecord.Models;

/// <summary>
/// Example entity demonstrating both Active Record pattern and metadata-driven design.
/// </summary>
[Table("Products")]
public class Product : ActiveRecord<Product>
{
    private string _name = "";
    private decimal _price;
    private int _quantity;

    public string Name
    {
        get => _name;
        set { _name = value; MarkDirty(); }
    }

    public decimal Price
    {
        get => _price;
        set { _price = value; MarkDirty(); }
    }

    public int Quantity
    {
        get => _quantity;
        set { _quantity = value; MarkDirty(); }
    }
}