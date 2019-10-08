﻿using System.Linq;
using Audit.Core;
using System;
using System.Reflection;
using System.Threading.Tasks;
#if NETSTANDARD1_5 || NETSTANDARD2_0 || NETSTANDARD2_1 || NET461
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
#elif NET45
using System.Data.Entity;
using System.Data.Entity.Core.Objects;
#endif

namespace Audit.EntityFramework.Providers
{
    /// <summary>
    /// Store the audits logs in the same EntityFramework model as the audited entities.
    /// </summary>
    /// <remarks>
    /// Settings:
    /// - AuditTypeMapper: A function that maps an entity type to its audited type (i.e. Order -> OrderAudit, etc)
    /// - AuditEntityAction: An action to perform on the audit entity before saving it
    /// - IgnoreMatchedProperties: Set to true to avoid the property values copy from the entity to the audited entity (default is false)
    /// </remarks>
    public class EntityFrameworkDataProvider : AuditDataProvider
    {
        private bool _ignoreMatchedProperties;
        private Func<Type, EventEntry, Type> _auditTypeMapper;
        private Func<AuditEvent, EventEntry, object, bool> _auditEntityAction;
        private Func<AuditEventEntityFramework, DbContext> _dbContextBuilder;

        public EntityFrameworkDataProvider()
        {

        }

        public EntityFrameworkDataProvider(Action<ConfigurationApi.IEntityFrameworkProviderConfigurator> config)
        {
            var efConfig = new ConfigurationApi.EntityFrameworkProviderConfigurator();
            if (config != null)
            {
                config.Invoke(efConfig);
                _auditEntityAction = efConfig._auditEntityAction;
                _auditTypeMapper = efConfig._auditTypeMapper;
                _dbContextBuilder = efConfig._dbContextBuilder;
                _ignoreMatchedProperties = efConfig._ignoreMatchedProperties;
            }
        }

        public Func<Type, EventEntry, Type> AuditTypeMapper
        {
            get { return _auditTypeMapper; }
            set { _auditTypeMapper = value; }
        }

        /// <summary>
        /// Function to execute on the audited entities before saving them. 
        /// Returns a boolean value indicating whther to include the entity on the logs or not.
        /// </summary>
        public Func<AuditEvent, EventEntry, object, bool> AuditEntityAction
        {
            get { return _auditEntityAction; }
            set { _auditEntityAction = value; }
        }

        public bool IgnoreMatchedProperties
        {
            get { return _ignoreMatchedProperties; }
            set { _ignoreMatchedProperties = value; }
        }

        /// <summary>
        /// Function that returns the DbContext to use for Audit Tables (default is the same context as the one audited)
        /// </summary>
        public Func<AuditEventEntityFramework, DbContext> DbContextBuilder
        {
            get { return _dbContextBuilder; }
            set { _dbContextBuilder = value; }
        }

        public override object InsertEvent(AuditEvent auditEvent)
        {
            bool save = false;
            if (!(auditEvent is AuditEventEntityFramework efEvent))
            {
                return null;
            }
            var localDbContext = efEvent.EntityFrameworkEvent.DbContext;
            var auditDbContext = DbContextBuilder?.Invoke(efEvent) ?? localDbContext;
            foreach(var entry in efEvent.EntityFrameworkEvent.Entries)
            {
                var type = GetEntityType(entry, localDbContext);
                if (type != null)
                {
                    entry.EntityType = type;
                    var mappedType = _auditTypeMapper?.Invoke(type, entry);
                    if (mappedType != null)
                    {
                        var auditEntity = CreateAuditEntity(type, mappedType, entry);
                        if (_auditEntityAction == null || _auditEntityAction.Invoke(efEvent, entry, auditEntity))
                        {
#if NET45
                            auditDbContext.Set(mappedType).Add(auditEntity);
#else
                            auditDbContext.Add(auditEntity);
#endif
                            save = true;
                        }
                    }
                }
            }
            if (save)
            {
                if (auditDbContext is IAuditBypass)
                {
                    (auditDbContext as IAuditBypass).SaveChangesBypassAudit();
                }
                else
                {
                    auditDbContext.SaveChanges();
                }
            }
            return null;
        }

        public override async Task<object> InsertEventAsync(AuditEvent auditEvent)
        {
            bool save = false;
            if (!(auditEvent is AuditEventEntityFramework efEvent))
            {
                return null;
            }
            var localDbContext = efEvent.EntityFrameworkEvent.DbContext;
            var auditDbContext = DbContextBuilder?.Invoke(efEvent) ?? localDbContext;
            foreach (var entry in efEvent.EntityFrameworkEvent.Entries)
            {
                var type = GetEntityType(entry, localDbContext);
                if (type != null)
                {
                    entry.EntityType = type;
                    var mappedType = _auditTypeMapper?.Invoke(type, entry);
                    if (mappedType != null)
                    {
                        var auditEntity = CreateAuditEntity(type, mappedType, entry);
                        if (_auditEntityAction == null || _auditEntityAction.Invoke(efEvent, entry, auditEntity))
                        {
#if NET45
                            auditDbContext.Set(mappedType).Add(auditEntity);
#else
                            await auditDbContext.AddAsync(auditEntity);
#endif
                            save = true;
                        }
                    }
                }
            }
            if (save)
            {
                if (auditDbContext is IAuditBypass)
                {
                    await (auditDbContext as IAuditBypass).SaveChangesBypassAuditAsync();
                }
                else
                {
                    await auditDbContext.SaveChangesAsync();
                }
            }
            return null;
        }

        private Type GetEntityType(EventEntry entry, DbContext localDbContext)
        {
            var entryType = entry.Entry.Entity.GetType();
            if (entryType.FullName.StartsWith("Castle.Proxies."))
            {
                entryType = entryType.GetTypeInfo().BaseType;
            }
            Type type;
#if NETSTANDARD2_0 || NETSTANDARD2_1
            IEntityType definingType = entry.Entry.Metadata.DefiningEntityType ?? localDbContext.Model.FindEntityType(entryType);
                type = definingType?.ClrType;
#elif NETSTANDARD1_5 || NET461
            IEntityType definingType = localDbContext.Model.FindEntityType(entryType);
            type = definingType?.ClrType;
#else
            type = ObjectContext.GetObjectType(entryType);
#endif
            return type;
        }

        private object CreateAuditEntity(Type definingType, Type auditType, EventEntry entry)
        {
            var entity = entry.Entry.Entity;
            var auditEntity = Activator.CreateInstance(auditType);
            if (!_ignoreMatchedProperties)
            {
                var auditFields = auditType.GetTypeInfo().GetProperties(BindingFlags.Public | BindingFlags.Instance).ToDictionary(k => k.Name);
                var entityFields = definingType.GetTypeInfo().GetProperties(BindingFlags.Public | BindingFlags.Instance);
                foreach (var field in entityFields.Where(af => auditFields.ContainsKey(af.Name)))
                {
                    var value = field.GetValue(entity);
                    auditFields[field.Name].SetValue(auditEntity, value);
                }
            }
            return auditEntity;
        }
    }
}
