using Microsoft.Extensions.DependencyInjection;
using Raven.Client.Documents;
using Raven.Client.Documents.Linq;
using Raven.Client.Documents.Session;
using System.Reflection;

namespace IanWold.Raven
{
    public static class Extensions
	{
		static Lazy<IDocumentStore>? _lazyDocumentStore;

		public static IServiceCollection AddRaven(this IServiceCollection services, Action<IDocumentStore> configure)
		{
			_lazyDocumentStore = new(() =>
			{
				void setEntityId(string id, object entity)
				{
					var entityType = entity.GetType();
					if (entityType.GetProperties().SingleOrDefault(p => p.Name == $"{entityType.Name}Id") is PropertyInfo idProperty && idProperty.PropertyType == typeof(Guid))
					{
						idProperty.SetValue(entity, new Guid(id));
					}
				}

				var documentStore = new DocumentStore
				{
					Conventions =
					{
						FindIdentityProperty = _ => false,
						AsyncDocumentIdGenerator = (databaseName, entity) =>
						{
							var newIdString = Guid.NewGuid().ToString();

							setEntityId(newIdString, entity);

							return Task.FromResult(newIdString);
						}
					}
				};

				configure(documentStore);

				documentStore.OnAfterConversionToEntity += (sender, e) =>
					setEntityId(e.Id, e.Entity);

				documentStore.OnBeforeStore += (sender, e) =>
					setEntityId(e.DocumentId, e.Entity);

				documentStore.Initialize();

				return documentStore;
			});

			services.AddSingleton(s => _lazyDocumentStore.Value);

			services.AddScoped(s =>
			{
				var documentStore = s.GetRequiredService<IDocumentStore>();
				var session = documentStore.OpenAsyncSession();

				session.Advanced.OnSessionDisposing += async (_, _) =>
				{
					if (session.Advanced.HasChanges)
					{
						await session.SaveChangesAsync();
					}
				};

				return session;
			});

			return services;
		}

		public async static Task DeleteByIdAsync<T>(this IAsyncDocumentSession session, Guid id, CancellationToken cancellationToken) =>
			session.Delete(await session.LoadAsync<T>(id.ToString(), cancellationToken));

		public async static Task<T> InsertAsync<T>(this IAsyncDocumentSession session, T entity, CancellationToken cancellationToken)
		{
			await session.StoreAsync(entity, cancellationToken);
			return entity;
		}

		public async static Task<IEnumerable<T>> QueryAsync<T>(this IAsyncDocumentSession session, Func<IRavenQueryable<T>, IQueryable<T>> query, CancellationToken cancellationToken) =>
			await query(session.Query<T>()).ToListAsync(cancellationToken);

		public async static Task<T> UpdateByIdAsync<T>(this IAsyncDocumentSession session, Guid id, Action<T> update, CancellationToken cancellationToken)
		{
			var entity = await session.LoadAsync<T>(id.ToString(), cancellationToken);
			update(entity);
			return entity;
		}
	}
}