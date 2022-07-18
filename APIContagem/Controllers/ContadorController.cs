using System.Diagnostics;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Extensibility;
using Confluent.Kafka;
using APIContagem.Models;
using APIContagem.Extensions;

namespace APIContagem.Controllers;

[ApiController]
[Route("[controller]")]
public class ContadorController : ControllerBase
{
    private static readonly Contador _CONTADOR = new Contador();
    private readonly ILogger<ContadorController> _logger;
    private readonly IConfiguration _configuration;
    private readonly TelemetryConfiguration _telemetryConfig;

    public ContadorController(ILogger<ContadorController> logger,
        IConfiguration configuration,
        TelemetryConfiguration telemetryConfig)
    {
        _logger = logger;
        _configuration = configuration;
        _telemetryConfig = telemetryConfig;
    }

    [HttpGet]
    public ResultadoContador Get()
    {
        var inicio = DateTime.Now;
        var watch = new Stopwatch();
        watch.Start();

        int valorAtualContador;
        int partition;

        lock (_CONTADOR)
        {
            _CONTADOR.Incrementar();
            valorAtualContador = _CONTADOR.ValorAtual;
            partition = _CONTADOR.Partition;
        }

        var resultado = new ResultadoContador()
        {
            ValorAtual = valorAtualContador,
            Producer = _CONTADOR.Local,
            Kernel = _CONTADOR.Kernel,
            Framework = _CONTADOR.Framework,
            Mensagem = _configuration["MensagemVariavel"]
        };

        string topic = _configuration["ApacheKafka:Topic"];
        string jsonContagem = JsonSerializer.Serialize(resultado);

        using (var producer = KafkaExtensions.CreateProducer(_configuration))
        {
            var result = producer.ProduceAsync(
                new TopicPartition(topic, new Partition(partition)),
                new Message<Null, string>
                { Value = jsonContagem }).Result;

            _logger.LogInformation(
                $"Apache Kafka - Envio para o topico {topic} concluido | Particao: {partition} | " +
                $"{jsonContagem} | Status: { result.Status.ToString()}");
        }

        watch.Stop();
        TelemetryClient client = new (_telemetryConfig);
        client.TrackDependency(
            "Kafka", $"Produce {topic}", jsonContagem, inicio, watch.Elapsed, true);

        return resultado;
    }
}