﻿using Newtonsoft.Json;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using ServicesInterfaces;
using ServicesInterfaces.Global;
using ServicesModels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MessagesListener.Utills
{
    public class RabbitMQEventHandler : IMessageRecievedEventHandler, IDisposable
    {
        public IModel _channel { get; }
        private readonly IAppSettings config;
        private IConnection _connection;
        private readonly IServicesFactory _factory;
        private readonly IDataAccessManager _dataManager;
        private IList<AmqpTcpEndpoint> endpoints;

        public RabbitMQEventHandler(IServicesFactory factory, IDataAccessManager data, IAppSettings _config)
        {
            _dataManager = data;
            _factory = factory;
            config = _config;
            InitAmqp();
            _connection = CreateConnection();
            _channel = _connection.CreateModel();
        }
        public void InitAmqp()
        {
            endpoints = new List<AmqpTcpEndpoint>();
            foreach (var port in config.QueuePorts)
            {
                endpoints.Add(new AmqpTcpEndpoint(config.HostName, port));
            }
        }
        public IConnection CreateConnection()
        {
            var factory = new ConnectionFactory();
            return factory.CreateConnection(endpoints);
        }

        public async void ConsumeMessage(object model, BasicDeliverEventArgs ea)
        {
            var _body = ea.Body.ToArray();
            var msg = Encoding.UTF8.GetString(_body);
            var message = JsonConvert.DeserializeObject<Message>(msg);
            Console.WriteLine("New message : " + message.MessageId);
            IService service = _factory.GetService(message.Service);

            var credentials = await GetUserNamePassword(message);

            var response = await service.AppStartUp(new Data() { Username = credentials.Username, Password = credentials.Password, Likes = message.Likes });

            try
            {

                if (response.Result == Result.Success)
                {
                    int result = 0;
                    result = await service.Like(new Data() { SessionId = response.SessionId, UserServiceId = response.UserServiceId, Likes = message.Likes });
                    if (result > 0)
                    {
                        // _channel.BasicNack(ea.DeliveryTag, true, true);
                        _channel.BasicReject(ea.DeliveryTag, true);
                    }
                    else
                    {
                        _channel.BasicAck(ea.DeliveryTag, false);
                    }
                }
                else
                {
                    await _dataManager.RemoveServiceFromUser(new Data() { Id = message.UserId, Service = message.Service });
                    _channel.BasicNack(ea.DeliveryTag, false, true);
                }
            }
            catch (Exception e)
            {
                _channel.BasicReject(ea.DeliveryTag, true);
            }
        }

        public void Dispose()
        {
            _channel.Dispose();
            _connection.Dispose();
        }
        ~RabbitMQEventHandler()
        {
            Dispose();
        }

        public Message DeserializeJsonMesage(byte[] msg)
        {
            var message = Encoding.UTF8.GetString(msg);
            return JsonConvert.DeserializeObject<Message>(message);
        }

        public byte[] SerializeMessage(Message msg)
        {
            var newJson = JsonConvert.SerializeObject(msg);
            return Encoding.UTF8.GetBytes(newJson);
        }

        public async Task<UserServiceCredentials> GetUserNamePassword(Message message)
        {
            return await _dataManager.GetUserServiceByServiceNameAndId(new Data() { Service = message.Service, Id = message.UserId });
        }
    }
}