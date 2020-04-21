﻿using System;
using Audit.Core.ConfigurationApi;
using Audit.EntityFramework.Providers;
using Audit.EntityFramework;
using Audit.EntityFramework.ConfigurationApi;
#if EF_CORE
using Microsoft.EntityFrameworkCore;
#else
using System.Data.Entity;
#endif

namespace Audit.Core
{
    public static class EntityFrameworkConfiguratorExtensions
    {
        /// <summary>
        /// Store the audits logs in the same EntityFramework model as the audited entities.
        /// </summary>
        internal static ICreationPolicyConfigurator UseEntityFramework(this IConfigurator configurator, Func<Type, EventEntry, Type> auditTypeMapper, Action<AuditEvent, EventEntry, object> auditEntityAction, Func<Type, bool> ignoreMatchedPropertiesFunc)
        {
            var efdp = new EntityFrameworkDataProvider()
            {
                AuditTypeMapper = auditTypeMapper,
                IgnoreMatchedPropertiesFunc = ignoreMatchedPropertiesFunc
            };
            if (auditEntityAction != null)
            {
                efdp.AuditEntityAction = (auditEvent, entry, auditEntity) =>
                {
                    auditEntityAction.Invoke(auditEvent, entry, auditEntity);
                    return true;
                };
            }
            Configuration.DataProvider = efdp;
            return new CreationPolicyConfigurator();
        }

        internal static ICreationPolicyConfigurator UseEntityFramework(this IConfigurator configurator, Func<Type, EventEntry, Type> auditTypeMapper, Func<AuditEvent, EventEntry, object, bool> auditEntityAction, Func<Type, bool> ignoreMatchedPropertiesFunc,
            Func<AuditEventEntityFramework, DbContext> dbContextBuilder)
        {
            var efdp = new EntityFrameworkDataProvider()
            {
                AuditTypeMapper = auditTypeMapper,
                IgnoreMatchedPropertiesFunc = ignoreMatchedPropertiesFunc,
                AuditEntityAction = auditEntityAction,
                DbContextBuilder = dbContextBuilder
            };
            Configuration.DataProvider = efdp;
            return new CreationPolicyConfigurator();
        }

        /// <summary>
        /// Store the audits logs in the same EntityFramework model as the audited entities.
        /// </summary>
        /// <param name="config">The EF provider configuration.</param>
        /// <param name="configurator">The Audit.NET configurator object.</param>
        public static ICreationPolicyConfigurator UseEntityFramework(this IConfigurator configurator, Action<IEntityFrameworkProviderConfigurator> config)
        {
            var efConfig = new EntityFrameworkProviderConfigurator();
            config.Invoke(efConfig);
            return UseEntityFramework(configurator, efConfig._auditTypeMapper, efConfig._auditEntityAction, efConfig._ignoreMatchedPropertiesFunc, efConfig._dbContextBuilder);
        }
    }
}
 