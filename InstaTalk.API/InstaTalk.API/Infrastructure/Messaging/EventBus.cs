using RabbitMQ.Client;
using System.Text;
using System.Text.Json;

namespace InstaTalk.API.Infrastructure.Messaging;

public interface IEventBus
{
    // Agora o contrato exige uma Task assíncrona
    Task PublishPostCreatedEventAsync(Guid postId, Guid ownerId, string content);
}

public class RabbitMqEventBus : IEventBus
{
    private readonly IConfiguration _configuration;

    public RabbitMqEventBus(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public async Task PublishPostCreatedEventAsync(Guid postId, Guid ownerId, string content)
    {
        var factory = new ConnectionFactory
        {
            HostName = _configuration["RabbitMQ:Host"] ?? "localhost",
            Port = int.Parse(_configuration["RabbitMQ:Port"] ?? "5672"),
            UserName = _configuration["RabbitMQ:Username"] ?? "guest",
            Password = _configuration["RabbitMQ:Password"] ?? "guest"
        };

        // V7: Usamos IChannel de forma totalmente assíncrona
        await using var connection = await factory.CreateConnectionAsync();
        await using var channel = await connection.CreateChannelAsync();

        await channel.QueueDeclareAsync(queue: "instatalk_events",
                             durable: true,
                             exclusive: false,
                             autoDelete: false,
                             arguments: null);

        var message = new
        {
            Event = "PostCreated",
            PostId = postId,
            OwnerId = ownerId,
            ContentPreview = content.Length > 50 ? content.Substring(0, 50) + "..." : content,
            Timestamp = DateTime.UtcNow
        };

        var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(message));

        // V7: Cria as propriedades básicas diretamente instanciando a classe
        var properties = new BasicProperties { Persistent = true };

        await channel.BasicPublishAsync(exchange: string.Empty,
                             routingKey: "instatalk_events",
                             mandatory: false,
                             basicProperties: properties,
                             body: body);
    }
}
