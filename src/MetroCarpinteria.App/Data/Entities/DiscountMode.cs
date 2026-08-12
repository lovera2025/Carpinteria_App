namespace MetroCarpinteria.App.Data.Entities;

/// <summary>Cómo está expresado el descuento comercial pactado con el cliente.</summary>
public enum DiscountMode
{
    /// <summary>Sin descuento. Es lo mismo que no tener valor guardado.</summary>
    None = 0,

    /// <summary>Un porcentaje sobre el subtotal («te hago un 10%»).</summary>
    Percentage = 1,

    /// <summary>Un importe fijo («dejámelo en redondo, sacá cinco mil»).</summary>
    Amount = 2
}
