﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

using Umbraco.Core;
using Umbraco.Core.Services;
using Umbraco.Core.Models;

using Jumoo.uSync.Core.Helpers;
using Jumoo.uSync.Core.Interfaces;
using System.Text.RegularExpressions;
using Jumoo.uSync.Core.Extensions;
using Umbraco.Core.Logging;

using Jumoo.uSync.Core.Mappers;

namespace Jumoo.uSync.Core.Serializers
{
    abstract public class ContentBaseSerializer<T> : SyncBaseSerializer<T>, ISyncSerializerWithParent<T>
    {
        internal IContentService _contentService;
        internal IMediaService _mediaService;

        public ContentBaseSerializer(string type) : base(type)
        {
            _contentService = ApplicationContext.Current.Services.ContentService;
            _mediaService = ApplicationContext.Current.Services.MediaService;
        }

        public SyncAttempt<T> Deserialize(XElement node, bool forceUpdate, bool onePass)
        {
            return Deserialize(node, -1, forceUpdate);
        }

        public SyncAttempt<T> Deserialize(XElement node, int parentId, bool forceUpdate = false)
        {
            // for content, we always call deserialize, because the first step will 
            // do the item lookup, and we want to return item so we can import 
            // as part of a tree. 
            return DeserializeCore(node, parentId, forceUpdate);
        }

        abstract internal SyncAttempt<T> DeserializeCore(XElement node, int parentId, bool forceUpdate);

        internal override SyncAttempt<T> DeserializeCore(XElement node)
        {
            return Deserialize(node, -1);
        }

        /// <summary>
        ///  second pass ID update in properties? 
        /// </summary>
        /// <param name="node"></param>
        /// <param name="item"></param>
        public void DeserializeMappedIds(T baseItem, XElement node)
        {
            IContentBase item = (IContentBase)baseItem;
            var properties = node.Elements().Where(x => x.Attribute("isDoc") == null);
            foreach (var property in properties)
            {
                var propertyTypeAlias = property.Name.LocalName;
                if (item.HasProperty(propertyTypeAlias))
                {
                    var prop = item.Properties[propertyTypeAlias];
                    string newValue = GetImportIds(prop.PropertyType, GetImportXml(property));
                    LogHelper.Debug<Events>("#### BASE: Setting property: [{0}] to {1}", () => propertyTypeAlias, ()=> newValue);
                    item.SetValue(propertyTypeAlias, newValue);
                }
            }
        }

        virtual public void PublishOrSave(T item, bool published, bool raiseEvents) { }



        internal string GetImportIds(PropertyType propType, string content)
        {
            var mapping = uSyncCoreContext.Instance.Configuration.Settings.ContentMappings
                .SingleOrDefault(x => x.EditorAlias == propType.PropertyEditorAlias);

            if (mapping != null)
            {
                LogHelper.Debug<Events>("Mapping Content Import: {0} {1}", () => mapping.EditorAlias, () => mapping.MappingType);

                IContentMapper mapper = ContentMapperFactory.GetMapper(mapping);

                if (mapper != null)
                {
                    return mapper.GetImportValue(propType.DataTypeDefinitionId, content);
                }
            }

            return content;
        }

        internal string GetImportXml(XElement parent)
        {
            var reader = parent.CreateReader();
            reader.MoveToContent();
            string xml = reader.ReadInnerXml();

            if (xml.StartsWith("<![CDATA["))
                return parent.Value;
            else
                return xml.Replace("&amp;", "&");
        }

        internal SyncAttempt<XElement> SerializeBase(IContentBase item, string contentTypeAlias)
        {
            var node = new XElement(contentTypeAlias);

            node.Add(new XAttribute("guid", item.Key.ToString().ToLower()));
            node.Add(new XAttribute("id", item.Id));
            node.Add(new XAttribute("nodeName", item.Name));
            node.Add(new XAttribute("isDoc", ""));
            node.Add(new XAttribute("updated", item.UpdateDate));

            foreach (var prop in item.Properties.Where(p => p != null))
            {
                XElement propNode = null;

                try
                {
                    propNode = prop.ToXml();
                }
                catch
                {
                    propNode = new XElement(prop.Alias, prop.Value);
                }

                string xml = "";
                xml = GetExportIds(prop.PropertyType, propNode);


                var updatedNode = XElement.Parse(
                    string.Format("<{0}>{1}</{0}>", propNode.Name.ToString(), xml), LoadOptions.PreserveWhitespace);

                node.Add(updatedNode);
            }
            return SyncAttempt<XElement>.Succeed(item.Name, node, item.GetType(), ChangeType.Export);
        }

        private string GetExportIds(PropertyType propType, XElement value)
        {
            // (need to strip cdata from value)
            var val = GetImportXml(value);

            var mapping = uSyncCoreContext.Instance.Configuration.Settings.ContentMappings
                .SingleOrDefault(x => x.EditorAlias == propType.PropertyEditorAlias);


            if (mapping != null)
            {
                LogHelper.Debug<Events>("Mapping Content Export: {0} {1}", () => mapping.EditorAlias, () => mapping.MappingType);

                IContentMapper mapper = ContentMapperFactory.GetMapper(mapping);

                if (mapper != null)
                {
                    return mapper.GetExportValue(propType.DataTypeDefinitionId, val);
                }
            }

            return GetInnerXml(value);
        }
    
        private string GetInnerXml(XElement parent)
        {
            var reader = parent.CreateReader();
            reader.MoveToContent();
            return reader.ReadInnerXml();
        }

        virtual public SyncAttempt<T> DesearlizeSecondPass(T item, XElement node)
        {
            return SyncAttempt<T>.Succeed(node.NameFromNode(), ChangeType.NoChange);
        }
    }
}
