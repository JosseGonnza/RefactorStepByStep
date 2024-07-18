using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.ApplicationInsights;
using Azure.Messaging.ServiceBus;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Azure;
using Microsoft.Extensions.Options;
using Optional;
using TyphoonManagerCommon;
using UnColaborador.MiChachiEquipo.TransactionalHub.CrossCutting.Options;
using UnColaborador.MiChachiEquipo.TransactionalHub.Entities;
using UnColaborador.MiChachiEquipo.TransactionalHub.Entities.PurchaseOrderDomain;
using UnColaborador.MiChachiEquipo.TransactionalHub.Entities.PurchaseOrderDomain.Enumerations;
using UnColaborador.MiChachiEquipo.TransactionalHub.Infrastructure.Repositories;
using UnColaborador.MiChachiEquipo.TransactionalHub.Integrator.PurchaseOrders.Consumer.Services;
using UnColaborador.MiChachiEquipo.TransactionalHub.Proxies;
using UnColaborador.MiChachiEquipo.TransactionalHub.Integrator.PurchaseOrders.Consumer.Services.UTCreation;

namespace UnColaborador.MiChachiEquipo.TransactionalHub.Integrator.PurchaseOrders.Consumer.Integrator;

public class QueueConsumer
{
    private readonly IPurchaseOrderTransactionRepository _purchaseOrderTransactionRepository;
    private readonly IAppGatewayProxy _appGatewayProxy;
    private readonly TelemetryClient _telemetryClient;
    private readonly IEmailProxy _emailProxy;
    private readonly ServiceBusSender _knownPropertySender;
    private readonly ServiceBusSender _unknownPropertySender;
    private readonly ErrorNotificationSettings _errorNotificationSettings;
    private const string ChocoName = "CHOCO";
    private const int ChocoAppId = 12847924;
    private const string GstockName = "GStock";
    private const int GstockAppid = 12543679;
    private const int MaxRetryCount = 3;
    private const string RetryCount = "RetryCount";
    
    public QueueConsumer(IPurchaseOrderTransactionRepository purchaseOrderTransactionRepository,
        IAppGatewayProxy appGatewayProxy, IEmailProxy emailProxy, TelemetryClient telemetryClient, 
        IAzureClientFactory<ServiceBusClient> serviceBusClientFactory, ServiceBusKnownPropertyOptions serviceBusKnownPropertyOptions,
        ServiceBusUnknownPropertyOptions serviceBusUnknownPropertyOptions, IOptions<ErrorNotificationSettings> errorNotificationSettings)
    {
        _purchaseOrderTransactionRepository = purchaseOrderTransactionRepository;
        _appGatewayProxy = appGatewayProxy;
        _telemetryClient = telemetryClient;
        _emailProxy = emailProxy;
        _knownPropertySender = CreateSender(serviceBusClientFactory, serviceBusKnownPropertyOptions);
        _unknownPropertySender = CreateSender(serviceBusClientFactory, serviceBusUnknownPropertyOptions);
        _errorNotificationSettings = errorNotificationSettings.Value;
    }

    [Function("ConsumeKnownPropertyQueue")]
    public async Task ConsumeKnownPropertyQueue(
        [ServiceBusTrigger("%ServiceBusKnownPropertyQueueName%", Connection = "ServiceBusKnownPropertyConnection")]
        ServiceBusReceivedMessage receivedMessage,  ServiceBusMessageActions messageActions)
    {
        IDictionary<string, string> telemetryProperties = new Dictionary<string, string>();

        var messagePurchaseOrder = GetMessagePurchaseOrderFromMessageBody(receivedMessage, telemetryProperties);
        _telemetryClient.TrackEvent(eventName: "ConsumeKnownPropertyQueue has received an event", telemetryProperties);

        try
        {
            var purchaseOrderTransaction = await GetPurchaseOrderTransaction(messagePurchaseOrder);
            purchaseOrderTransaction.Traces.Add(Trace.AsMessageReceived());
            await _purchaseOrderTransactionRepository.Save(purchaseOrderTransaction);
            try
            {
                if (IsPurchaseOrderCreationTimeMoreThanFifteenMinutesAgo(purchaseOrderTransaction))
                {
                    purchaseOrderTransaction.SetErrorWithDetail(ErrorDetail.DeliveryTimeExceeded);
                    await messageActions.DeadLetterMessageAsync(receivedMessage, deadLetterReason: ErrorDetail.DeliveryTimeExceeded.ToString());
                    await _emailProxy.Send(new Email(purchaseOrderTransaction.PurchaseOrder.Reference,
                        purchaseOrderTransaction.OrderId, _errorNotificationSettings.EmailRecipients,
                        _errorNotificationSettings.SenderAddress));
                }
                else if (IsLastRetry(receivedMessage))
                {
                    telemetryProperties.Add("RetryCountMax", GetRetryCount(receivedMessage).ToString());
                    await messageActions.DeadLetterMessageAsync(receivedMessage, deadLetterReason: ErrorDetail.RetryCountExceeded.ToString());
                    purchaseOrderTransaction.SetErrorWithDetail(ErrorDetail.RetryCountExceeded);
                    await _emailProxy.Send(new Email(purchaseOrderTransaction.PurchaseOrder.Reference,
                        purchaseOrderTransaction.OrderId, _errorNotificationSettings.EmailRecipients,
                        _errorNotificationSettings.SenderAddress));
                }
                else
                {
                    await ProcessPurchaseOrderTransactionForKnownProperty(purchaseOrderTransaction, telemetryProperties);
                }
                
                await _purchaseOrderTransactionRepository.Save(purchaseOrderTransaction);
            }
            catch (ErrorDetailException exception)
            {
                await ProcessErrorDetailException(purchaseOrderTransaction, exception, telemetryProperties);
            }
        }
        catch (Exception exception)
        {
            var serviceBusMessageExtensions = new ServiceBusMessageExtensions(_knownPropertySender, telemetryProperties);
            await serviceBusMessageExtensions.RetryWithExponentialBackoff(messageActions, receivedMessage, MaxRetryCount);
            _telemetryClient.TrackException(exception, telemetryProperties);
            throw;
        }
    }

    [Function("ConsumeUnknownPropertyQueue")]
    public async Task ConsumeUnknownPropertyQueue(
        [ServiceBusTrigger("%ServiceBusUnknownPropertyQueueName%", Connection = "ServiceBusUnknownPropertyConnection")]
        ServiceBusReceivedMessage receivedMessage, ServiceBusMessageActions messageActions)
    {
        IDictionary<string, string> telemetryProperties = new Dictionary<string, string>();

        var messagePurchaseOrder = GetMessagePurchaseOrderFromMessageBody(receivedMessage, telemetryProperties);
        _telemetryClient.TrackEvent(eventName: "ConsumeUnknownPropertyQueue has received an event",
            telemetryProperties);
        
        try
        {
            var purchaseOrderTransaction = await GetPurchaseOrderTransaction(messagePurchaseOrder);
            purchaseOrderTransaction.Traces.Add(Trace.AsMessageReceived());
            await _purchaseOrderTransactionRepository.Save(purchaseOrderTransaction);
            
            try
            {
                if (IsPurchaseOrderCreationTimeMoreThanFifteenMinutesAgo(purchaseOrderTransaction))
                {
                    purchaseOrderTransaction.SetErrorWithDetail(ErrorDetail.DeliveryTimeExceeded);
                    await messageActions.DeadLetterMessageAsync(receivedMessage, deadLetterReason: ErrorDetail.DeliveryTimeExceeded.ToString());
                    await _emailProxy.Send(new Email(purchaseOrderTransaction.PurchaseOrder.Reference,
                        purchaseOrderTransaction.OrderId, _errorNotificationSettings.EmailRecipients,
                        _errorNotificationSettings.SenderAddress));
                }
                else if (IsLastRetry(receivedMessage))
                {
                    telemetryProperties.Add("RetryCountMax", GetRetryCount(receivedMessage).ToString());
                    await messageActions.DeadLetterMessageAsync(receivedMessage, deadLetterReason: ErrorDetail.RetryCountExceeded.ToString());
                    purchaseOrderTransaction.SetErrorWithDetail(ErrorDetail.RetryCountExceeded);
                    await _emailProxy.Send(new Email(purchaseOrderTransaction.PurchaseOrder.Reference,
                        purchaseOrderTransaction.OrderId, _errorNotificationSettings.EmailRecipients,
                        _errorNotificationSettings.SenderAddress));
                }
                else
                {
                    await ProcessPurchaseOrderTransactionForUnknownProperty(purchaseOrderTransaction, telemetryProperties);
                }

                await _purchaseOrderTransactionRepository.Save(purchaseOrderTransaction);
            }
            catch (ErrorDetailException exception)
            {
                await ProcessErrorDetailException(purchaseOrderTransaction, exception, telemetryProperties);
            }
        }
        catch (Exception exception)
        {
            var serviceBusMessageExtensions = new ServiceBusMessageExtensions(_unknownPropertySender, telemetryProperties);
            await serviceBusMessageExtensions.RetryWithExponentialBackoff(messageActions, receivedMessage, MaxRetryCount);
            _telemetryClient.TrackException(exception, telemetryProperties);
            throw;
        }
    }

    private bool IsLastRetry(ServiceBusReceivedMessage receivedMessage)
    {
        return GetRetryCount(receivedMessage) > MaxRetryCount;
    }

    private int GetRetryCount(ServiceBusReceivedMessage receivedMessage) =>
        receivedMessage.ApplicationProperties.TryGetValue(RetryCount, out var retriedTimes)
            ? int.Parse(Convert.ToString(retriedTimes) ?? "0") : 1;

    private static ServiceBusSender CreateSender(IAzureClientFactory<ServiceBusClient> serviceBusClientFactory, IServiceBusOptions serviceBusPropertyOptions)
    {
        return serviceBusClientFactory.CreateClient(serviceBusPropertyOptions.ServiceBusQueueName)
            .CreateSender(serviceBusPropertyOptions.ServiceBusQueueName);
    }

    private async Task<PurchaseOrderTransaction> GetPurchaseOrderTransaction(MessagePurchaseOrder messagePurchaseOrder)
    {
        var possiblePurchaseOrderTransaction =
            await _purchaseOrderTransactionRepository.Get(messagePurchaseOrder.OrderId, messagePurchaseOrder.RequesterId);
        var purchaseOrderTransaction = possiblePurchaseOrderTransaction.ValueOr(() =>
            throw new Exception($"Purchase order with id {@messagePurchaseOrder.OrderId} does not exist in the database"));
        return purchaseOrderTransaction;
    }

    private async Task ProcessPurchaseOrderTransactionForKnownProperty(PurchaseOrderTransaction purchaseOrderTransaction,
        IDictionary<string, string> telemetryProperties)
    {
        var possiblePropertyId = await _appGatewayProxy.GetUserId((Guid)(purchaseOrderTransaction.PurchaseOrder).Client.PropertyId);
        var propertyId = possiblePropertyId.ValueOr(() => throw new ErrorDetailException(ErrorDetail.ExternalUserIdNotFound));

        var possibleCustomer = await _appGatewayProxy.GetCustomer(propertyId);
        var customer = possibleCustomer.ValueOr(() => throw new ErrorDetailException(ErrorDetail.PropertyIdNotFound));

        var possibleClient = await _appGatewayProxy.GetClient(customer.ParentId);
        var client = possibleClient.ValueOr(() => throw new ErrorDetailException(ErrorDetail.ClientIdNotFound));

        await ErrorDetailIfTaxIdCountryIsNotValid(purchaseOrderTransaction);

        var possibleSupplier = await GetPossibleSupplier(purchaseOrderTransaction);
        var heritableClientSupplierRelations =
            await ObtainHeritableClientSupplierRelations(possibleSupplier, customer, purchaseOrderTransaction);
        var realSender = new RealSender(GstockName, GstockAppid);
        var universalIntegratedTransactionParameters = new UniversalIntegratedTransactionParameters(
            possibleSupplier, possibleCustomer, 
            heritableClientSupplierRelations, purchaseOrderTransaction,
            client, realSender);
                
        var universalIntegratedTransaction = GenerateUniversalIntegratedTransaction(universalIntegratedTransactionParameters);

        var tid = await SendTransactionToApp(universalIntegratedTransaction, telemetryProperties);

        purchaseOrderTransaction.TraceAsSentToApp(tid);
    }
    
    private async Task ProcessPurchaseOrderTransactionForUnknownProperty(PurchaseOrderTransaction purchaseOrderTransaction,
        IDictionary<string, string> telemetryProperties)
    {
        await ErrorDetailIfTaxIdCountryIsNotValid(purchaseOrderTransaction);

        var possibleSupplier = await GetPossibleSupplier(purchaseOrderTransaction);
        var realSender = new RealSender(ChocoName, ChocoAppId);

        var universalIntegratedTransactionParameters = new UniversalIntegratedTransactionParameters(
            possibleSupplier, Option.None<Customer>(),
            new List<HeritableClientSupplierRelation>(), purchaseOrderTransaction,
            new Client(), realSender);

        var universalIntegratedTransaction = GenerateUniversalIntegratedTransaction(universalIntegratedTransactionParameters);

        var tid = await SendTransactionToApp(universalIntegratedTransaction, telemetryProperties);

        purchaseOrderTransaction.TraceAsSentToApp(tid);
    }

    private async Task ErrorDetailIfTaxIdCountryIsNotValid(PurchaseOrderTransaction purchaseOrderTransaction)
    {
        if (!await IsValidTaxIdCountry(purchaseOrderTransaction))
        {
            throw new ErrorDetailException(ErrorDetail.InvalidSupplierTaxIdOrCountry);
        }
    }

    private static bool IsPurchaseOrderCreationTimeMoreThanFifteenMinutesAgo(PurchaseOrderTransaction purchaseOrderTransaction) => 
        (DateTime.UtcNow - purchaseOrderTransaction.GetCreationDateFromTrace()).Minutes > 15;
    
    private static UniversalIntegratedTransaction GenerateUniversalIntegratedTransaction(UniversalIntegratedTransactionParameters parameters)
    {
        var universalIntegratedTransactionBuilder =
            UniversalIntegratedTransactionFactory.SelectUniversalIntegratedTransactionType(parameters);
        
        var universalIntegratedTransaction = universalIntegratedTransactionBuilder.Build(parameters);
        return universalIntegratedTransaction;
    }
    
    private async Task<long> SendTransactionToApp(UniversalIntegratedTransaction universalIntegratedTransaction,
        IDictionary<string, string> telemetryProperties)
    {
        var outgoingTransactionBufferQueueItem = new OutgoingTransactionBufferQueueItem(universalIntegratedTransaction);
        var tid = await _appGatewayProxy.QueueInApp(outgoingTransactionBufferQueueItem);

        telemetryProperties.Add("TID", tid.ToString());
        _telemetryClient.TrackEvent(eventName: "The purchase order has been processed by Transaction Buffer",
            telemetryProperties);
        return tid;
    }

    private async Task ProcessErrorDetailException(PurchaseOrderTransaction purchaseOrderTransaction,
        ErrorDetailException exception, IDictionary<string, string> telemetryProperties)
    {
        purchaseOrderTransaction.SetErrorWithDetail(exception.Detail);
        await _purchaseOrderTransactionRepository.Save(purchaseOrderTransaction);
        _telemetryClient.TrackException(exception, telemetryProperties);
    }

    private static MessagePurchaseOrder GetMessagePurchaseOrderFromMessageBody(ServiceBusReceivedMessage myQueueItem,
        IDictionary<string, string> telemetryProperties)
    {
        var json = myQueueItem.Body.ToString();
        var message = JsonSerializer.Deserialize<MessagePurchaseOrder>(json);

        telemetryProperties.Add("PurchaseOrderId", message.OrderId.ToString());

        return message;
    }

    private async Task<Option<Supplier>> GetPossibleSupplier(PurchaseOrderTransaction purchaseOrderTransaction)
    {
        return await _appGatewayProxy.GetSupplier(
            purchaseOrderTransaction.PurchaseOrder.Supplier.TaxId,
            purchaseOrderTransaction.PurchaseOrder.Supplier.Country);
    }

    private async Task<bool> IsValidTaxIdCountry(PurchaseOrderTransaction purchaseOrderTransaction)
    {
        return await _appGatewayProxy.ValidateSupplierCountryTaxId(
            purchaseOrderTransaction.PurchaseOrder.Supplier.TaxId,
            purchaseOrderTransaction.PurchaseOrder.Supplier.Country);
    }

    private async Task<List<HeritableClientSupplierRelation>> ObtainHeritableClientSupplierRelations(
        Option<Supplier> supplier, Customer customer, PurchaseOrderTransaction purchaseOrderTransaction)
    {
        return await supplier.Match(async supplierWithValue =>
                await _appGatewayProxy.GetRelationsBetweenClientAndSupplier(customer, supplierWithValue, purchaseOrderTransaction), 
            () => Task.FromResult(new List<HeritableClientSupplierRelation>()));
    }
}
