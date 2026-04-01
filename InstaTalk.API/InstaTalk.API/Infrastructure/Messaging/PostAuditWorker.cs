using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System.Text;

namespace InstaTalk.API.Infrastructure.Messaging;

// O BackgroundService roda em uma thread separada pelo tempo de vida da API
public class PostAuditWorker : BackgroundService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<PostAuditWorker> _logger;
    private IConnection? _connection;

    private IChannel? _channel;

    public PostAuditWorker(IConfiguration configuration, ILogger<PostAuditWorker> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var factory = new ConnectionFactory
        {
            HostName = _configuration["RabbitMQ:Host"] ?? "localhost",
            Port = int.Parse(_configuration["RabbitMQ:Port"] ?? "5672"),
            UserName = _configuration["RabbitMQ:Username"] ?? "guest",
            Password = _configuration["RabbitMQ:Password"] ?? "guest"
        };

        try
        {
            _connection = await factory.CreateConnectionAsync(stoppingToken);
            _channel = await _connection.CreateChannelAsync(cancellationToken: stoppingToken);

            await _channel.QueueDeclareAsync(queue: "instatalk_events", durable: true, exclusive: false, autoDelete: false, arguments: null, cancellationToken: stoppingToken);
            _logger.LogInformation("✅ Conectado ao RabbitMQ (v7 Async). Aguardando eventos...");
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"⚠️ RabbitMQ não está rodando. Erro: {ex.Message}");
            return; // Se falhar, sai fora para não travar a API
        }

        var consumer = new AsyncEventingBasicConsumer(_channel);

        consumer.ReceivedAsync += async (model, ea) =>
        {
            var body = ea.Body.ToArray();
            var message = Encoding.UTF8.GetString(body);

            _logger.LogWarning($"\n[WORKER ASSÍNCRONO] Processando evento pesado na fila: {message}");

            // Trocamos Thread.Sleep por Task.Delay para não bloquear a thread do C#
            await Task.Delay(2000, stoppingToken);

            _logger.LogWarning("[WORKER ASSÍNCRONO] Tarefa concluída com sucesso!\n");

            await _channel.BasicAckAsync(deliveryTag: ea.DeliveryTag, multiple: false, cancellationToken: stoppingToken);
        };

        await _channel.BasicConsumeAsync(queue: "instatalk_events", autoAck: false, consumer: consumer, cancellationToken: stoppingToken);

        // Mantém o Worker rodando infinito enquanto a API estiver viva
        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(1000, stoppingToken);
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        // Limpa a sujeira ao desligar a API
        if (_channel is not null) await _channel.CloseAsync(cancellationToken);
        if (_connection is not null) await _connection.CloseAsync(cancellationToken);
        await base.StopAsync(cancellationToken);
    }
}
