﻿namespace BookingService
{
    using System;
    using System.Configuration;
    using System.IO;
    using Booking.Activities.FetchAvatar;
    using Booking.Activities.ReserveRoom;
    using Booking.Services;
    using MassTransit;
    using MassTransit.Courier;
    using MassTransit.Util;
    using Topshelf;
    using Topshelf.Logging;


    class BookingService :
        ServiceControl
    {
        readonly LogWriter _log = HostLogger.Get<BookingService>();

        IBusControl _busControl;
        BusHandle _busHandle;
        BookingRequestHandlerSettings _settings;
        FetchAvatarActivitySettings _fetchAvatarActivitySettings;

        public bool Start(HostControl hostControl)
        {
            _settings = new Settings();
            _fetchAvatarActivitySettings = new FetchAvatarSettings();

            _log.Info("Creating bus...");


            _busControl = Bus.Factory.CreateUsingRabbitMq(x =>
            {
                var host = x.Host(GetHostAddress(), h =>
                {
                    h.Username(ConfigurationManager.AppSettings["RabbitMQUsername"]);
                    h.Password(ConfigurationManager.AppSettings["RabbitMQPassword"]);
                });

                x.ReceiveEndpoint(host, ConfigurationManager.AppSettings["BookMeetingQueueName"], e =>
                {
                    e.Consumer(() =>
                    {
                        var handler = new BookingRequestHandler(_settings);

                        return new BookMeetingConsumer(handler);
                    });
                });

                x.ReceiveEndpoint(host, ConfigurationManager.AppSettings["FetchAvatarActivityQueue"], e =>
                {
                    e.ExecuteActivityHost<FetchAvatarActivity, FetchAvatarArguments>(() => new FetchAvatarActivity(_fetchAvatarActivitySettings));
                });

                x.ReceiveEndpoint(host, ConfigurationManager.AppSettings["ReserveRoomCompensateQueue"], c =>
                {
                    var compensateAddress = c.InputAddress;

                    c.ExecuteActivityHost<ReserveRoomActivity, ReserveRoomArguments>();

                    x.ReceiveEndpoint(host, ConfigurationManager.AppSettings["ReserveRoomExecuteQueue"], e =>
                    {
                        e.ExecuteActivityHost<ReserveRoomActivity, ReserveRoomArguments>(compensateAddress);
                    });
                });
            });

            _log.Info("Starting bus...");

            _busHandle = _busControl.Start();

            TaskUtil.Await(() => _busHandle.Ready);

            return true;
        }

        public bool Stop(HostControl hostControl)
        {
            _log.Info("Stopping bus...");

            _busHandle?.Stop(TimeSpan.FromSeconds(30));

            return true;
        }

        static Uri GetHostAddress()
        {
            var uriBuilder = new UriBuilder
            {
                Scheme = "rabbitmq",
                Host = ConfigurationManager.AppSettings["RabbitMQHost"]
            };

            return uriBuilder.Uri;
        }


        class Settings :
            BookingRequestHandlerSettings
        {
            public Settings()
            {
                FetchAvatarActivityName = ConfigurationManager.AppSettings["FetchAvatarActivityName"];
                FetchAvatarExecuteAddress = new Uri(ConfigurationManager.AppSettings["FetchAvatarExecuteAddress"]);
                ReserveRoomActivityName = ConfigurationManager.AppSettings["ReserveRoomActivityName"];
                ReserveRoomExecuteAddress = new Uri(ConfigurationManager.AppSettings["ReserveRoomExecuteAddress"]);
            }

            public string FetchAvatarActivityName { get; }

            public Uri FetchAvatarExecuteAddress { get; }
            public string ReserveRoomActivityName { get; }
            public Uri ReserveRoomExecuteAddress { get; }
        }


        class FetchAvatarSettings :
            FetchAvatarActivitySettings
        {
            public FetchAvatarSettings()
            {
                CacheFolderPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "avatars");

                Directory.CreateDirectory(CacheFolderPath);
            }

            public string CacheFolderPath { get; }
        }
    }
}