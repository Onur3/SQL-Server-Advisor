using SqlServerAdvisor.Domain.Entities;

namespace SqlServerAdvisor.Application.Contracts;

public interface IRecommendationFactory
{
    Recommendation? Create(Finding finding);
}
