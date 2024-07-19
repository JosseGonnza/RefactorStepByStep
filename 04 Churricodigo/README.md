En este ejemplo tenemos una cola de mensajería en service bus.

Hemos sustituido algunos nombres para que sea anónimo, como por ejemplo hemos usado `app` en lugar del nombre de la aplicación en concreto.

## Contexto

El código es una implementación en C# de un consumidor de mensajes de Service Bus en Azure.
Se encarga de procesar mensajes relacionados con órdenes de compra (Purchase Orders) que llegan a dos colas diferentes: 
- para propiedades conocidas
- para propiedades desconocidas

La clase QueueConsumer contiene dos métodos principales: 
- ConsumeKnownPropertyQueue
- ConsumeUnknownPropertyQueue
 
Son funciones de Azure Functions triggereadas por mensajes en sus respectivas colas.

Intención del Código

- Procesamiento de Mensajes.
- Manejo de Errores y Retries.
- Notificación y Telemetría.

[El código aquí](original.cs)
