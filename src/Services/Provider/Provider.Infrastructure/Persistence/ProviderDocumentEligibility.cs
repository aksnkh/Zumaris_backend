namespace Zumaris.Provider.Infrastructure.Persistence;
internal static class ProviderDocumentEligibility
{
 // The alias p always refers to the local Provider table. Never use a legacy overall flag
 // to substitute for any independent required-document decision.
 public const string Predicate="""
 EXISTS(SELECT 1 FROM provider_verification_slots bs
   JOIN provider_document_reviews br ON br.document_id=bs.verified_document_id AND br.provider_id=bs.provider_id AND br.document_type=bs.document_type
   WHERE bs.provider_id=p.id AND bs.document_type='IdentityCheck' AND br.status='Verified')
 AND EXISTS(SELECT 1 FROM provider_verification_slots ins
   JOIN provider_document_reviews ir ON ir.document_id=ins.verified_document_id AND ir.provider_id=ins.provider_id AND ir.document_type=ins.document_type
   WHERE ins.provider_id=p.id AND ins.document_type='Insurance' AND ir.status='Verified')
 AND EXISTS(SELECT 1 FROM provider_verification_slots cs
   JOIN provider_document_reviews cr ON cr.document_id=cs.verified_document_id AND cr.provider_id=cs.provider_id AND cr.document_type=cs.document_type
   WHERE cs.provider_id=p.id AND cs.document_type='Certificate' AND cr.status='Verified')
 AND EXISTS(SELECT 1 FROM provider_verification_profile vp WHERE vp.provider_id=p.id AND CHAR_LENGTH(TRIM(vp.postcode))>0)
 """;
}
