using Dixon.CommandCenter.API.Models;
using Microsoft.Data.SqlClient;

namespace Dixon.CommandCenter.API.Data;

public sealed class CatalogRepository(SqlConnectionFactory connectionFactory)
{
    public async Task<IReadOnlyList<CustomerResponse>> GetCustomersAsync(CancellationToken cancellationToken)
    {
        var customers = new Dictionary<int, CustomerBuilder>();
        var categories = new Dictionary<int, CategoryBuilder>();
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);

        await using (var command = new SqlCommand("SELECT CustomerId, CustomerName FROM Customer WHERE IsActive = 1 ORDER BY CustomerName", connection))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                var customer = new CustomerBuilder(reader.GetInt32(0), reader.GetString(1));
                customers.Add(customer.Id, customer);
            }
        }

        await using (var command = new SqlCommand("SELECT CategoryId, CustomerId, CategoryName FROM Category WHERE IsActive = 1 ORDER BY CategoryName", connection))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                if (customers.TryGetValue(reader.GetInt32(1), out var customer))
                {
                    var category = new CategoryBuilder(reader.GetInt32(0), reader.GetString(2));
                    categories.Add(category.Id, category);
                    customer.Categories.Add(category);
                }
            }
        }

        await using (var command = new SqlCommand("""
            SELECT l.LineId, l.CategoryId, l.LineName, l.SAPLocation
            FROM ProductionLine l
            INNER JOIN Category c ON c.CategoryId = l.CategoryId AND c.IsActive = 1
            WHERE l.IsActive = 1
            ORDER BY l.LineName
            """, connection))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                if (categories.TryGetValue(reader.GetInt32(1), out var category))
                {
                    category.Lines.Add(new ProductionLineResponse(reader.GetInt32(0), reader.GetString(2), reader.IsDBNull(3) ? null : reader.GetString(3)));
                }
            }
        }

        await using (var command = new SqlCommand("SELECT ModelId, CustomerId, ModelName FROM Model WHERE IsActive = 1 ORDER BY ModelName", connection))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                if (customers.TryGetValue(reader.GetInt32(1), out var customer))
                {
                    customer.Models.Add(new ModelResponse(reader.GetInt32(0), reader.GetString(2)));
                }
            }
        }

        return customers.Values.Select(customer => new CustomerResponse(
            customer.Id,
            customer.Name,
            customer.Models,
            customer.Categories.Select(category => new CategoryResponse(category.Id, category.Name, category.Lines)).ToArray()))
            .ToArray();
    }

    private sealed class CustomerBuilder(int id, string name)
    {
        public int Id { get; } = id;
        public string Name { get; } = name;
        public List<ModelResponse> Models { get; } = [];
        public List<CategoryBuilder> Categories { get; } = [];
    }

    private sealed class CategoryBuilder(int id, string name)
    {
        public int Id { get; } = id;
        public string Name { get; } = name;
        public List<ProductionLineResponse> Lines { get; } = [];
    }
}